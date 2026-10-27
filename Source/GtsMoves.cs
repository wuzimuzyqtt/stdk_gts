using UnityEngine;

namespace GTS
{
	public enum GtsMove : byte
	{
		Stomp,          // opener: right foot up, slam down on the victim
		StompLeft,      // opener / pin: the same with the left foot
		PauseSlam,      // opener / pin: foot hovers over the victim, then comes down hard
		Kick,           // opener: right leg swings back and kicks the victim away
		Rub,            // pin: foot rubs side to side across the victim
		ToeGrind,       // pin: weight on the toes, twisting
		RepeatStomp,    // pin: three quick stomps in a row
		StandOn         // pin: steps over the victim and stomps with alternating feet
	}

	// One leg's goal for this frame, in the victim frame: x along the zombie's right, y up, z along the zombie's
	// forward, origin at the victim's feet. Weight 0 = leave the leg to the normal animation.
	public struct GtsLegPose
	{
		public Vector3 target;   // the ankle, or the ball of the foot when toePivot
		public float weight;
		public float pitch;      // foot pitch, degrees, 0 = sole flat, negative = toes down / heel up
		public float twist;      // foot twist around the vertical, degrees, positive = toes turn to her right
		public bool toePivot;    // target is the ball of the foot: pitch and twist swing the heel around it
	}

	public struct GtsMovePose
	{
		public GtsLegPose right;
		public GtsLegPose left;
		public float lean;       // upper body, degrees, positive = forward
		public float crouch;     // hips lowered this far (metres), both knees bend
	}

	// The move table: timing (shared by server and clients so damage lines up with the foot) and the procedural
	// foot trajectories (clients only).
	public static class GtsMoves
	{
		private const float FootH = 0.06f;   // ankle height above the ground when planted

		public static float Length(GtsMove _m)
		{
			switch (_m)
			{
				case GtsMove.Stomp:
				case GtsMove.StompLeft: return 0.9f;
				case GtsMove.PauseSlam: return 1.6f;
				case GtsMove.Kick: return 0.85f;
				case GtsMove.Rub: return 2.4f;
				case GtsMove.ToeGrind: return 2.4f;
				case GtsMove.RepeatStomp: return 1.25f;
				case GtsMove.StandOn: return 2.6f;
			}
			return 1f;
		}

		// Seconds into the move at which a foot lands (and the server deals damage).
		public static float[] Impacts(GtsMove _m)
		{
			switch (_m)
			{
				case GtsMove.Stomp:
				case GtsMove.StompLeft: return new[] { 0.55f };
				case GtsMove.PauseSlam: return new[] { 1.3f };
				case GtsMove.Kick: return new[] { 0.42f };
				case GtsMove.Rub: return new[] { 0.6f, 1.2f, 1.8f, 2.4f };
				case GtsMove.ToeGrind: return new[] { 0.8f, 1.6f, 2.4f };
				case GtsMove.RepeatStomp: return new[] { 0.35f, 0.75f, 1.15f };
				case GtsMove.StandOn: return new[] { 0.45f, 0.85f, 1.25f, 1.65f, 2.05f, 2.45f };
			}
			return new float[0];
		}

		public static int Damage(GtsMove _m)
		{
			switch (_m)
			{
				case GtsMove.PauseSlam: return GtsConfig.DamageHeavy;
				case GtsMove.Kick: return GtsConfig.KickDamage;
				case GtsMove.Rub: return GtsConfig.DamageLight;
				case GtsMove.StandOn: return GtsConfig.DamageLight;
			}
			return GtsConfig.DamageNormal;
		}

		// Which side the foot that lands is on: +1 right, -1 left, 0 both / centred.
		public static float Side(GtsMove _m)
		{
			return _m == GtsMove.StompLeft ? -1f : _m == GtsMove.StandOn ? 0f : 1f;
		}

		public static bool IsOpener(GtsMove _m)
		{
			return _m == GtsMove.Stomp || _m == GtsMove.StompLeft || _m == GtsMove.PauseSlam || _m == GtsMove.Kick;
		}

		public static GtsMove PickOpener()
		{
			float r = Random.value;
			if (r < 0.35f) return GtsMove.Stomp;
			if (r < 0.55f) return GtsMove.StompLeft;
			if (r < 0.75f) return GtsMove.PauseSlam;
			return GtsMove.Kick;
		}

		public static GtsMove PickPinMove(GtsMove _previous)
		{
			for (int tries = 0; tries < 8; tries++)
			{
				float r = Random.value;
				GtsMove m = r < 0.22f ? GtsMove.Rub
					: r < 0.42f ? GtsMove.ToeGrind
					: r < 0.62f ? GtsMove.RepeatStomp
					: r < 0.76f ? GtsMove.PauseSlam
					: r < 0.88f ? GtsMove.StompLeft
					: GtsMove.StandOn;
				if (m != _previous)
				{
					return m;
				}
			}
			return GtsMove.Rub;
		}

		// ---------------- poses (client) ----------------
		// The server keeps her StompFootReach (KickStandDistance for a kick) behind the victim, so with her hips
		// about 0.85 m up a foot on the victim is near the end of her reach; GtsMoveAnim lowers and moves the hips
		// so the knee stays bent.

		private const float BallH = 0.03f;   // ball of the foot above the ground

		private static Vector3 Planted(float _side) => new Vector3(_side * 0.04f, FootH, 0f);
		private static Vector3 Raised(float _side) => new Vector3(_side * 0.06f, 0.5f, -0.12f);
		private static Vector3 Hover(float _side) => new Vector3(_side * 0.04f, 0.55f, -0.02f);

		public static GtsMovePose Evaluate(GtsMove _m, float _t)
		{
			GtsMovePose p = new GtsMovePose();
			switch (_m)
			{
				case GtsMove.Stomp:
					p.right = StompLeg(_t, 1f);
					p.lean = StompLean(_t);
					p.crouch = StompCrouch(_t, 0.45f);
					break;
				case GtsMove.StompLeft:
					p.left = StompLeg(_t, -1f);
					p.lean = StompLean(_t);
					p.crouch = StompCrouch(_t, 0.45f);
					break;
				case GtsMove.PauseSlam:
					p.right = PauseSlamLeg(_t, out p.lean);
					p.crouch = StompCrouch(_t, 1.2f) * 1.3f;
					break;
				case GtsMove.Kick:
					p.right = KickLeg(_t, out p.lean);
					break;
				case GtsMove.Rub:
				{
					// on the ball of the foot, the heel swinging side to side; she bears down at each end (0.6 s apart)
					float ramp = EaseOut(_t / 0.4f);
					float c = Mathf.Cos(_t * Mathf.PI * 2f / 1.2f);
					float press = Mathf.Pow(Mathf.Abs(c), 4f) * ramp;
					p.right = new GtsLegPose { target = new Vector3(0.03f, BallH, 0.02f), weight = 1f, pitch = -10f - 8f * press,
						twist = c * ramp * GtsConfig.GrindTwistAngle, toePivot = true };
					p.lean = GtsConfig.StompLeanForward + 5f * press;
					p.crouch = 0.05f + 0.04f * press;
					break;
				}
				case GtsMove.ToeGrind:
				{
					// up on the toes, heel high, twisting; presses at each end (0.8 s apart)
					float ramp = EaseOut(_t / 0.4f);
					float c = Mathf.Cos(_t * Mathf.PI * 2f / 1.6f);
					float press = Mathf.Pow(Mathf.Abs(c), 4f) * ramp;
					p.right = new GtsLegPose { target = new Vector3(0.03f, BallH, 0.03f), weight = 1f, pitch = -38f - 8f * press,
						twist = c * ramp * GtsConfig.ToeGrindTwistAngle, toePivot = true };
					p.lean = GtsConfig.StompLeanForward + 8f + 4f * press;
					p.crouch = 0.08f + 0.05f * press;
					break;
				}
				case GtsMove.RepeatStomp:
				{
					// three 0.4s cycles: lift 0.25s, slam 0.1s, planted 0.05s
					float c = Mathf.Repeat(_t, 0.4f);
					Vector3 up = Planted(1f) + new Vector3(0f, 0.28f, -0.05f);
					Vector3 pos = c < 0.25f ? Vector3.Lerp(Planted(1f), up, EaseOut(c / 0.25f))
						: c < 0.35f ? Vector3.Lerp(up, Planted(1f), EaseIn((c - 0.25f) / 0.1f))
						: Planted(1f);
					float lift = (pos.y - FootH) / 0.28f;
					p.right = Leg(_t >= 1.2f ? Planted(1f) : pos, 1f, -8f * lift, 0f);
					p.lean = GtsConfig.StompLeanForward;
					p.crouch = 0.04f + 0.03f * (1f - lift);
					break;
				}
				case GtsMove.StandOn:
				{
					// the server walks the zombie over the victim; feet land alternately either side of centre
					p.left = AlternateLeg(_t, 0.05f, -1f);
					p.right = AlternateLeg(_t, 0.45f, 1f);
					p.lean = 10f;
					p.crouch = 0.03f;
					break;
				}
			}
			return p;
		}

		private static GtsLegPose StompLeg(float _t, float _side)
		{
			if (_t < 0.35f)
			{
				return Leg(Raised(_side), EaseOut(_t / 0.35f), -8f, 0f);
			}
			if (_t < 0.45f)
			{
				return Leg(Raised(_side), 1f, -8f, 0f);
			}
			if (_t < 0.55f)
			{
				float k = EaseIn((_t - 0.45f) / 0.1f);
				return Leg(Vector3.Lerp(Raised(_side), Planted(_side), k), 1f, -8f * (1f - k), 0f);
			}
			return Leg(Planted(_side), 1f, 0f, 0f);
		}

		private static float StompLean(float _t)
		{
			if (_t < 0.45f)
			{
				return -GtsConfig.StompLeanBack * EaseOut(Mathf.Min(1f, _t / 0.35f));
			}
			return Mathf.Lerp(-GtsConfig.StompLeanBack, GtsConfig.StompLeanForward, EaseIn(Mathf.Min(1f, (_t - 0.45f) / 0.1f)));
		}

		// She sinks into the stomp as the foot comes down (slam starts at _slam, lands 0.1 s later).
		private static float StompCrouch(float _t, float _slam)
		{
			return _t < _slam ? 0f : 0.06f * EaseOut((_t - _slam) / 0.15f);
		}

		private static GtsLegPose PauseSlamLeg(float _t, out float _lean)
		{
			if (_t < 0.4f)
			{
				_lean = -10f * EaseOut(_t / 0.4f);
				return Leg(Hover(1f), EaseOut(_t / 0.4f), -8f, 0f);
			}
			if (_t < 1.2f)
			{
				// hanging there, a little unsteady
				_lean = -10f;
				return Leg(Hover(1f) + new Vector3(0f, Mathf.Sin(_t * 9f) * 0.02f, 0f), 1f, -8f, Mathf.Sin(_t * 5f) * 6f);
			}
			if (_t < 1.3f)
			{
				float k = EaseIn((_t - 1.2f) / 0.1f);
				_lean = Mathf.Lerp(-10f, 20f, k);
				return Leg(Vector3.Lerp(Hover(1f), Planted(1f), k), 1f, -8f * (1f - k), 0f);
			}
			_lean = 20f;
			return Leg(Planted(1f), 1f, 0f, 0f);
		}

		// Victim KickStandDistance in front. Heel swings up behind her, the knee comes through high (chamber),
		// the shin snaps out through the victim, then the foot comes back down under her.
		private static GtsLegPose KickLeg(float _t, out float _lean)
		{
			float hip = -GtsConfig.KickStandDistance;
			Vector3 back = new Vector3(0.08f, 0.38f, hip - 0.32f);
			Vector3 chamber = new Vector3(0.06f, 0.36f, hip + 0.08f);
			Vector3 hit = new Vector3(0f, 0.16f, 0f);
			Vector3 follow = new Vector3(0f, 0.26f, 0.1f);
			Vector3 home = new Vector3(0.1f, FootH, hip + 0.03f);
			if (_t < 0.25f)
			{
				float k = EaseOut(_t / 0.25f);
				_lean = 8f * k;
				return Leg(back, k, -40f, 0f);
			}
			if (_t < 0.34f)
			{
				float k = EaseInOut((_t - 0.25f) / 0.09f);
				_lean = Mathf.Lerp(8f, 0f, k);
				return Leg(Vector3.Lerp(back, chamber, k), 1f, Mathf.Lerp(-40f, -30f, k), 0f);
			}
			if (_t < 0.42f)
			{
				float k = EaseIn((_t - 0.34f) / 0.08f);
				_lean = Mathf.Lerp(0f, -12f, k);
				return Leg(Vector3.Lerp(chamber, hit, k), 1f, Mathf.Lerp(-30f, -20f, k), 0f);
			}
			if (_t < 0.52f)
			{
				_lean = -12f;
				return Leg(Vector3.Lerp(hit, follow, EaseOut((_t - 0.42f) / 0.1f)), 1f, -20f, 0f);
			}
			float r = EaseInOut((_t - 0.52f) / 0.23f);
			_lean = -12f * (1f - r);
			return Leg(Vector3.Lerp(follow, home, r), 1f - Mathf.Clamp01((_t - 0.7f) / 0.15f), Mathf.Lerp(-20f, 0f, r), 0f);
		}

		// One foot of the StandOn dance: planted beside centre, stomping every 0.8s starting at _offset.
		private static GtsLegPose AlternateLeg(float _t, float _offset, float _side)
		{
			Vector3 planted = new Vector3(_side * 0.09f, FootH, 0f);
			Vector3 up = planted + new Vector3(0f, 0.28f, -0.03f);
			float c = _t - _offset;
			if (c < 0f)
			{
				return Leg(planted, 1f, 0f, 0f);
			}
			c = Mathf.Repeat(c, 0.8f);
			if (c < 0.3f)
			{
				return Leg(Vector3.Lerp(planted, up, EaseOut(c / 0.3f)), 1f, -8f * EaseOut(c / 0.3f), 0f);
			}
			if (c < 0.4f)
			{
				float k = EaseIn((c - 0.3f) / 0.1f);
				return Leg(Vector3.Lerp(up, planted, k), 1f, -8f * (1f - k), 0f);
			}
			return Leg(planted, 1f, 0f, 0f);
		}

		private static GtsLegPose Leg(Vector3 _target, float _weight, float _pitch, float _twist)
		{
			return new GtsLegPose { target = _target, weight = _weight, pitch = _pitch, twist = _twist };
		}

		public static float EaseInOut(float _x)
		{
			_x = Mathf.Clamp01(_x);
			return _x * _x * (3f - 2f * _x);
		}

		public static float EaseOut(float _x)
		{
			_x = Mathf.Clamp01(_x);
			return 1f - (1f - _x) * (1f - _x);
		}

		public static float EaseIn(float _x)
		{
			_x = Mathf.Clamp01(_x);
			return _x * _x;
		}
	}
}
