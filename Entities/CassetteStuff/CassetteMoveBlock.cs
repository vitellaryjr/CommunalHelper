﻿using Celeste.Mod.Entities;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections;
using System.Collections.Generic;

// TODO
// movement stuff
// Dealing with the period of non collidability when respawning
// fix static movers during respawn
// fix block staying collidable after breaking
namespace Celeste.Mod.CommunalHelper {
	[CustomEntity("CommunalHelper/CassetteMoveBlock")]
	[TrackedAs(typeof(CassetteBlock))]
	class CassetteMoveBlock : CustomCassetteBlock {
		public enum Directions {
			Left,
			Right,
			Up,
			Down
		}

		private enum MovementState {
			Idling,
			Moving,
			Breaking
		}

		[Pooled]
		private class Debris : Actor {
			private Image sprite;
			private CassetteMoveBlock block;
			private Vector2 home;
			private Vector2 speed;

			private bool shaking;
			private bool returning;

			private float returnEase;
			private float returnDuration;
			private SimpleCurve returnCurve;

			private bool firstHit;
			private float alpha;

			private Collision onCollideH;
			private Collision onCollideV;

			private float spin;
			private Color color;
			private Color pressedColor;

			public Debris()
				: base(Vector2.Zero) {
				base.Tag = Tags.TransitionUpdate;
				base.Collider = new Hitbox(4f, 4f, -2f, -2f);
				Add(sprite = new Image(Calc.Random.Choose(GFX.Game.GetAtlasSubtextures("objects/CommunalHelper/cassetteMoveBlock/debris"))));
				sprite.CenterOrigin();
				sprite.FlipX = Calc.Random.Chance(0.5f);
				onCollideH = delegate
				{
					speed.X = (0f - speed.X) * 0.5f;
				};
				onCollideV = delegate
				{
					if (firstHit || speed.Y > 50f) {
						Audio.Play("event:/game/general/debris_stone", Position, "debris_velocity", Calc.ClampedMap(speed.Y, 0f, 600f));
					}
					if (speed.Y > 0f && speed.Y < 40f) {
						speed.Y = 0f;
					} else {
						speed.Y = (0f - speed.Y) * 0.25f;
					}
					firstHit = false;
				};
			}

			protected override void OnSquish(CollisionData data) {
			}

			public Debris Init(CassetteMoveBlock block, Vector2 position, Vector2 center, Vector2 returnTo, Color color) {
				Collidable = true;
				this.block = block;
				Position = position;
				speed = (position - center).SafeNormalize(60f + Calc.Random.NextFloat(60f));
				home = returnTo;
				sprite.Position = Vector2.Zero;
				sprite.Rotation = Calc.Random.NextAngle();
				returning = false;
				shaking = false;
				sprite.Scale.X = 1f;
				sprite.Scale.Y = 1f;
				this.color = color;
				pressedColor = color.Mult(Calc.HexToColor("667da5"));
				sprite.Color = block.Collidable ? color : pressedColor;
				alpha = 1f;
				firstHit = false;
				spin = Calc.Random.Range(3.49065852f, 10.4719753f) * (float)Calc.Random.Choose(1, -1);
				return this;
			}

			public override void Update() {
				base.Update();
				if (!returning) {
					if (Collidable) {
						speed.X = Calc.Approach(speed.X, 0f, Engine.DeltaTime * 100f);
						if (!OnGround()) {
							speed.Y += 400f * Engine.DeltaTime;
						}
						MoveH(speed.X * Engine.DeltaTime, onCollideH);
						MoveV(speed.Y * Engine.DeltaTime, onCollideV);
					}
					if (shaking && base.Scene.OnInterval(0.05f)) {
						sprite.X = -1 + Calc.Random.Next(3);
						sprite.Y = -1 + Calc.Random.Next(3);
					}
				} else {
					Position = returnCurve.GetPoint(Ease.CubeOut(returnEase));
					returnEase = Calc.Approach(returnEase, 1f, Engine.DeltaTime / returnDuration);
					sprite.Scale = Vector2.One * (1f + returnEase * 0.5f);
				}
				sprite.Color = block.Activated ? color : pressedColor;
				if ((base.Scene as Level).Transitioning) {
					alpha = Calc.Approach(alpha, 0f, Engine.DeltaTime * 4f);
					sprite.Color *= alpha;
				}
				sprite.Rotation += spin * Calc.ClampedMap(Math.Abs(speed.Y), 50f, 150f) * Engine.DeltaTime;
				
			}

			public void StopMoving() {
				Collidable = false;
			}

			public void StartShaking() {
				shaking = true;
			}

			public void ReturnHome(float duration) {
				if (base.Scene != null) {
					Camera camera = (base.Scene as Level).Camera;
					if (base.X < camera.X) {
						base.X = camera.X - 8f;
					}
					if (base.Y < camera.Y) {
						base.Y = camera.Y - 8f;
					}
					if (base.X > camera.X + 320f) {
						base.X = camera.X + 320f + 8f;
					}
					if (base.Y > camera.Y + 180f) {
						base.Y = camera.Y + 180f + 8f;
					}
				}
				returning = true;
				returnEase = 0f;
				returnDuration = duration;
				Vector2 vector = (home - Position).SafeNormalize();
				Vector2 control = (Position + home) / 2f + new Vector2(vector.Y, 0f - vector.X) * (Calc.Random.NextFloat(16f) + 16f) * Calc.Random.Facing();
				returnCurve = new SimpleCurve(Position, home, control);
			}
		}

		private const float Accel = 300f;
		private const float MoveSpeed = 60f;
		private const float FastMoveSpeed = 75f;
		private const float SteerSpeed = (float)Math.PI * 16f;
		private const float MaxAngle = (float)Math.PI / 4f;
		private const float NoSteerTime = 0.2f;
		private const float CrashTime = 0.15f;
		private const float CrashResetTime = 0.1f;
		private const float RegenTime = 3f;

		private bool fast;
		private Directions direction;
		private float homeAngle;
		private Vector2 startPosition;
		private MovementState state = MovementState.Idling;

		private float speed;
		private float targetSpeed;
		private float angle;
		private float targetAngle;

		private Player noSquish;

		private Image arrow;
		private Image arrowPressed;
		private Image cross;
		private Image crossPressed;

		private float flash;
		private SoundSource moveSfx;
		private bool triggered;
		private float particleRemainder;

		private ParticleType P_Activate;
		private ParticleType P_Move;
		private ParticleType P_MovePressed;
		private ParticleType P_Break;
		private ParticleType P_BreakPressed;

		public CassetteMoveBlock(Vector2 position, EntityID id, int width, int height, Directions direction, bool fast, int index, float tempo)
			: base(position, id, width, height, index, 1, tempo, dynamicHitbox: true) {
			startPosition = position;
			this.direction = direction;
			this.fast = fast;
			switch (direction) {
				default:
					homeAngle = (targetAngle = (angle = 0f));
					break;
				case Directions.Left:
					homeAngle = (targetAngle = (angle = (float)Math.PI));
					break;
				case Directions.Up:
					homeAngle = (targetAngle = (angle = -(float)Math.PI / 2f));
					break;
				case Directions.Down:
					homeAngle = (targetAngle = (angle = (float)Math.PI / 2f));
					break;
			}
			Add(moveSfx = new SoundSource());
			Add(new Coroutine(Controller()));

			P_Activate = new ParticleType(MoveBlock.P_Activate) {
				Color = color
			};
			P_Move = new ParticleType(MoveBlock.P_Move) {
				Color = color
			};
			P_MovePressed = new ParticleType(MoveBlock.P_Move) {
				Color = pressedColor
			};
			P_Break = new ParticleType(MoveBlock.P_Break) {
				Color = color
			};
			P_BreakPressed = new ParticleType(MoveBlock.P_Break) {
				Color = pressedColor
			};
		}

		public CassetteMoveBlock(EntityData data, Vector2 offset, EntityID id)
			: this(data.Position + offset, id, data.Width, data.Height, data.Enum("direction", Directions.Left), data.Bool("fast"), data.Int("index"), data.Float("tempo", 1f)) {
		}

        public override void Awake(Scene scene) {
			int index = (int)Math.Floor((0f - angle + (float)Math.PI * 2f) % ((float)Math.PI * 2f) / ((float)Math.PI * 2f) * 8f + 0.5f);
			arrow = new Image(GFX.Game.GetAtlasSubtextures("objects/CommunalHelper/cassetteMoveBlock/arrow")[index]);
			arrowPressed = new Image(GFX.Game.GetAtlasSubtextures("objects/CommunalHelper/cassetteMoveBlock/arrowPressed")[index]);
			cross = new Image(GFX.Game["objects/CommunalHelper/cassetteMoveBlock/x"]);
			crossPressed = new Image(GFX.Game["objects/CommunalHelper/cassetteMoveBlock/xPressed"]);

			base.Awake(scene);
			AddCenterSymbol(arrow, arrowPressed);
			AddCenterSymbol(cross, crossPressed);
		}

        private IEnumerator Controller() {
			while (true) {
				triggered = false;
				state = MovementState.Idling;
				while (!triggered && !HasPlayerRider()) {
					yield return null;
				}
				Audio.Play("event:/game/04_cliffside/arrowblock_activate", Position);
				state = MovementState.Moving;
				StartShaking(0.2f);
				ActivateParticles();
				yield return 0.2f;
				targetSpeed = (fast ? 75f : 60f);
				moveSfx.Play("event:/game/04_cliffside/arrowblock_move");
				moveSfx.Param("arrow_stop", 0f);
				StopPlayerRunIntoAnimation = false;
				float crashTimer = 0.15f;
				float crashResetTimer = 0.1f;
				while (true) {
					if (Scene.OnInterval(0.02f)) {
						MoveParticles();
					}
					speed = Calc.Approach(speed, targetSpeed, 300f * Engine.DeltaTime);
					angle = Calc.Approach(angle, targetAngle, (float)Math.PI * 16f * Engine.DeltaTime);
					Vector2 move = Calc.AngleToVector(angle, speed) * Engine.DeltaTime;
					bool hit;
					if (direction == Directions.Right || direction == Directions.Left) {
						hit = MoveCheck(move.XComp());
						noSquish = Scene.Tracker.GetEntity<Player>();
						MoveVCollideSolids(move.Y, thruDashBlocks: false);
						noSquish = null;
						if (Scene.OnInterval(0.03f)) {
							if (move.Y > 0f) {
								ScrapeParticles(Vector2.UnitY);
							} else if (move.Y < 0f) {
								ScrapeParticles(-Vector2.UnitY);
							}
						}
					} else {
						hit = MoveCheck(move.YComp());
						noSquish = Scene.Tracker.GetEntity<Player>();
						MoveHCollideSolids(move.X, thruDashBlocks: false);
						noSquish = null;
						if (Scene.OnInterval(0.03f)) {
							if (move.X > 0f) {
								ScrapeParticles(Vector2.UnitX);
							} else if (move.X < 0f) {
								ScrapeParticles(-Vector2.UnitX);
							}
						}
						if (direction == Directions.Down && Top > (float)(SceneAs<Level>().Bounds.Bottom + 32)) {
							hit = true;
						}
					}
					if (hit) {
						moveSfx.Param("arrow_stop", 1f);
						crashResetTimer = 0.1f;
						if (!(crashTimer > 0f)) {
							break;
						}
						crashTimer -= Engine.DeltaTime;
					} else {
						moveSfx.Param("arrow_stop", 0f);
						if (crashResetTimer > 0f) {
							crashResetTimer -= Engine.DeltaTime;
						} else {
							crashTimer = 0.15f;
						}
					}
					Level level = Scene as Level;
					if (Left < (float)level.Bounds.Left || Top < (float)level.Bounds.Top || Right > (float)level.Bounds.Right) {
						break;
					}
					yield return null;
				}
				Audio.Play("event:/game/04_cliffside/arrowblock_break", Position);
				moveSfx.Stop();
				state = MovementState.Breaking;
				speed = (targetSpeed = 0f);
				angle = (targetAngle = homeAngle);
				StartShaking(0.2f);
				StopPlayerRunIntoAnimation = true;
				yield return 0.2f;
				BreakParticles();
				List<Debris> debris = new List<Debris>();
				for (int x = 0; (float)x < Width; x += 8) {
					for (int y = 0; (float)y < Height; y += 8) {
						Vector2 offset = new Vector2((float)x + 4f, (float)y + 4f);
						Debris d = Engine.Pooler.Create<Debris>().Init(this, Position + offset, Center, startPosition + offset, color);
						debris.Add(d);
						Scene.Add(d);
					}
				}
				Vector2 newPosition = startPosition + blockOffset;
				Util.log("blockOffset: " + blockOffset);
				MoveStaticMovers(newPosition - Position);
				DisableStaticMovers();
				Position = newPosition;
				Visible = (Collidable = false);
				yield return 2.2f;
				foreach (Debris d2 in debris) {
					d2.StopMoving();
				}
				while (CollideCheck<Actor>() || CollideCheck<Solid>()) {
					yield return null;
				}
				Collidable = virtualCollidable;
				Util.log("collidable: " + Collidable);
				EventInstance sound = Audio.Play("event:/game/04_cliffside/arrowblock_reform_begin", debris[0].Position);
				Coroutine component;
				Coroutine routine = component = new Coroutine(SoundFollowsDebrisCenter(sound, debris));
				Add(component);
				foreach (Debris d4 in debris) {
					d4.StartShaking();
				}
				yield return 0.2f;
				foreach (Debris d5 in debris) {
					d5.ReturnHome(0.65f);
				}
				yield return 0.6f;
				routine.RemoveSelf();
				foreach (Debris d3 in debris) {
					d3.RemoveSelf();
				}
				Audio.Play("event:/game/04_cliffside/arrowblock_reappear", Position);
				Visible = true;
				EnableStaticMovers();
				speed = (targetSpeed = 0f);
				angle = (targetAngle = homeAngle);
				noSquish = null;
				flash = 1f;
			}
		}

		private IEnumerator SoundFollowsDebrisCenter(EventInstance instance, List<Debris> debris) {
			while (true) {
				instance.getPlaybackState(out PLAYBACK_STATE state);
				if (state == PLAYBACK_STATE.STOPPED) {
					break;
				}
				Vector2 center = Vector2.Zero;
				foreach (Debris d in debris) {
					center += d.Position;
				}
				center /= (float)debris.Count;
				Audio.Position(instance, center);
				yield return null;
			}
		}

		public override void Update() {
			base.Update();
			if (moveSfx != null && moveSfx.Playing) {
				float num = (Calc.AngleToVector(angle, 1f) * new Vector2(-1f, 1f)).Angle();
				int num2 = (int)Math.Floor((0f - num + (float)Math.PI * 2f) % ((float)Math.PI * 2f) / ((float)Math.PI * 2f) * 8f + 0.5f);
				moveSfx.Param("arrow_influence", num2 + 1);
			}
			flash = Calc.Approach(flash, 0f, Engine.DeltaTime * 5f);
		}

		public override void OnStaticMoverTrigger(StaticMover sm) {
			triggered = true;
		}

		public override void MoveHExact(int move) {
			if (noSquish != null && ((move < 0 && noSquish.X < base.X) || (move > 0 && noSquish.X > base.X))) {
				while (move != 0 && noSquish.CollideCheck<Solid>(noSquish.Position + Vector2.UnitX * move)) {
					move -= Math.Sign(move);
				}
			}
			base.MoveHExact(move);
		}

		public override void MoveVExact(int move) {
			if (noSquish != null && move < 0 && noSquish.Y <= base.Y) {
				while (move != 0 && noSquish.CollideCheck<Solid>(noSquish.Position + Vector2.UnitY * move)) {
					move -= Math.Sign(move);
				}
			}
			base.MoveVExact(move);
		}

		private bool MoveCheck(Vector2 speed) {
			if (speed.X != 0f) {
				if (MoveHCollideSolids(speed.X, thruDashBlocks: false)) {
					for (int i = 1; i <= 3; i++) {
						for (int num = 1; num >= -1; num -= 2) {
							Vector2 value = new Vector2(Math.Sign(speed.X), i * num);
							if (!CollideCheck<Solid>(Position + value)) {
								MoveVExact(i * num);
								MoveHExact(Math.Sign(speed.X));
								return false;
							}
						}
					}
					return true;
				}
				return false;
			}
			if (speed.Y != 0f) {
				if (MoveVCollideSolids(speed.Y, thruDashBlocks: false)) {
					for (int j = 1; j <= 3; j++) {
						for (int num2 = 1; num2 >= -1; num2 -= 2) {
							Vector2 value2 = new Vector2(j * num2, Math.Sign(speed.Y));
							if (!CollideCheck<Solid>(Position + value2)) {
								MoveHExact(j * num2);
								MoveVExact(Math.Sign(speed.Y));
								return false;
							}
						}
					}
					return true;
				}
				return false;
			}
			return false;
		}

		public override void Render() {
			Vector2 position = Position;
			Position += base.Shake;
			base.Render();float num = flash * 4f;
			Draw.Rect(base.X - num, base.Y - num, base.Width + num * 2f, base.Height + num * 2f, Color.White * flash);
			Position = position;
		}

		public override void HandleUpdateVisualState() {
			blockData.Get<Entity>("side").Visible &= Visible;

			bool crossVisible = state == MovementState.Breaking;
			arrow.Visible &= !crossVisible;
			arrowPressed.Visible &= !crossVisible;
			cross.Visible &= crossVisible;
			crossPressed.Visible &= crossVisible;
		}

		private void ActivateParticles() {
			bool flag2 = !CollideCheck<Player>(Position - Vector2.UnitX);
			bool flag3 = !CollideCheck<Player>(Position + Vector2.UnitX);
			bool flag4 = !CollideCheck<Player>(Position - Vector2.UnitY);
			if (flag2) {
				SceneAs<Level>().ParticlesBG.Emit(P_Activate, (int)(base.Height / 2f), base.CenterLeft, Vector2.UnitY * (base.Height - 4f) * 0.5f, (float)Math.PI);
			}
			if (flag3) {
				SceneAs<Level>().ParticlesBG.Emit(P_Activate, (int)(base.Height / 2f), base.CenterRight, Vector2.UnitY * (base.Height - 4f) * 0.5f, 0f);
			}
			if (flag4) {
				SceneAs<Level>().ParticlesBG.Emit(P_Activate, (int)(base.Width / 2f), base.TopCenter, Vector2.UnitX * (base.Width - 4f) * 0.5f, -(float)Math.PI / 2f);
			}
			SceneAs<Level>().ParticlesBG.Emit(P_Activate, (int)(base.Width / 2f), base.BottomCenter, Vector2.UnitX * (base.Width - 4f) * 0.5f, (float)Math.PI / 2f);
		}

		private void BreakParticles() {
			Vector2 center = base.Center;
			ParticleType particle = Collidable ? P_Break : P_BreakPressed;
			for (int i = 0; (float)i < base.Width; i += 4) {
				for (int j = 0; (float)j < base.Height; j += 4) {
					Vector2 vector = Position + new Vector2(2 + i, 2 + j);
					SceneAs<Level>().Particles.Emit(particle, 1, vector, Vector2.One * 2f, (vector - center).Angle());
				}
			}
		}

		private void MoveParticles() {
			Vector2 position;
			Vector2 positionRange;
			float num;
			float num2;
			if (direction == Directions.Right) {
				position = base.CenterLeft + Vector2.UnitX;
				positionRange = Vector2.UnitY * (base.Height - 4f);
				num = (float)Math.PI;
				num2 = base.Height / 32f;
			} else if (direction == Directions.Left) {
				position = base.CenterRight;
				positionRange = Vector2.UnitY * (base.Height - 4f);
				num = 0f;
				num2 = base.Height / 32f;
			} else if (direction == Directions.Down) {
				position = base.TopCenter + Vector2.UnitY;
				positionRange = Vector2.UnitX * (base.Width - 4f);
				num = -(float)Math.PI / 2f;
				num2 = base.Width / 32f;
			} else {
				position = base.BottomCenter;
				positionRange = Vector2.UnitX * (base.Width - 4f);
				num = (float)Math.PI / 2f;
				num2 = base.Width / 32f;
			}
			particleRemainder += num2;
			int num3 = (int)particleRemainder;
			particleRemainder -= num3;
			positionRange *= 0.5f;
			if (num3 > 0) {
				SceneAs<Level>().ParticlesBG.Emit(Collidable ? P_Move : P_MovePressed, num3, position, positionRange, num);
			}
		}

		private void ScrapeParticles(Vector2 dir) {
			if (Collidable) {
				Collidable = false;
				if (dir.X != 0f) {
					float x = (!(dir.X > 0f)) ? (base.Left - 1f) : base.Right;
					for (int i = 0; (float)i < base.Height; i += 8) {
						Vector2 vector = new Vector2(x, base.Top + 4f + (float)i);
						if (base.Scene.CollideCheck<Solid>(vector)) {
							SceneAs<Level>().ParticlesFG.Emit(ZipMover.P_Scrape, vector);
						}
					}
				} else {
					float y = (!(dir.Y > 0f)) ? (base.Top - 1f) : base.Bottom;
					for (int j = 0; (float)j < base.Width; j += 8) {
						Vector2 vector2 = new Vector2(base.Left + 4f + (float)j, y);
						if (base.Scene.CollideCheck<Solid>(vector2)) {
							SceneAs<Level>().ParticlesFG.Emit(ZipMover.P_Scrape, vector2);
						}
					}
				}
				Collidable = true;
			}
		}
	}
}
