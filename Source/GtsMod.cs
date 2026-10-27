using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GTS
{
	public class GtsModInit : IModApi
	{
		public void InitMod(Mod _modInstance)
		{
			new Harmony("zhang.gts").PatchAll(Assembly.GetExecutingAssembly());

			GameObject go = new GameObject("GtsDriver");
			Object.DontDestroyOnLoad(go);
			go.AddComponent<GtsDriver>();

			Log.Out("[GTS] Loaded");
		}
	}

	public static class GtsConfig
	{
		public const string ShrinkBuff = "buffGtsShrink";
		public const string ScaleCVar = "gtsShrinkScale";
		public const float DefaultScale = 0.25f;
		public const float MinScale = 0.01f;
		public const float MaxScale = 0.9f;

		// Movement speed multiplier while shrunk. 0 (or unset) = follow the scale.
		public const string SpeedCVar = "gtsSpeedMult";
		public const float MinSpeed = 0.01f;
		public const float MaxSpeed = 3f;

		// Stomp detection (server side)
		public const float CheckInterval = 0.1f;
		public const float PlayerHeight = 1.8f;    // full-size player, used to derive the tiny player's size
		public const float PlayerRadius = 0.3f;
		public const float StompFootRadius = 0.35f; // stomper's foot reach, added to the victim's own radius
		public const float StompMinDY = -0.4f;     // stomper feet relative to victim feet
		public const float StompHeadroom = 0.3f;   // stomper feet may be this far above the victim's head (standing on them)
		public const float StompMinSpeed = 0.5f;   // m/s, standing still next to a tiny player is safe
		public const float PlayerStompMaxScale = 0.3f; // other players only squash someone shrunk below this
		public const float MaleTouchMargin = 0.2f;  // male zombie falls apart within its own radius + victim radius + this

		// Female zombie encounters (see GtsPins / GtsMoves). Within StompTriggerRange she opens with a random move;
		// a stomp landing on the victim pins them, a kick throws them. Pinned victims take damage on every landing.
		public const float StompTriggerRange = 1.0f;   // horizontal metres, zombie to victim
		public const float StompOnlyMaxScale = 0.3f;   // at or below this size female zombies only stomp, never swing or bite
		public const float StompFootReach = 0.35f;     // foot lands this far in front of the zombie
		public const float StompHitLeniency = 0.15f;   // extra radius around the landing spot
		public const float StompCooldown = 1.5f;       // seconds before the same zombie starts again
		public const int DamageLight = 5;              // per landing: rub, stand-on
		public const int DamageNormal = 8;             // per landing: stomps, toe grind, repeat stomp
		public const int DamageHeavy = 16;             // the hovering slam
		public const int KickDamage = 12;
		public const float KickRange = 0.9f;           // victim must be within this, in front of her
		public const float KickForce = 0.15f;          // horizontal launch (character-controller units per frame)
		public const float KickUp = 0.12f;             // vertical launch
		public const float StandOnBack = 0.12f;        // during StandOn she stands this far behind the victim's centre
		public const float StandOnStepSpeed = 1.2f;    // m/s while stepping over them
		public const float KickStandDistance = 0.45f;  // during a kick she keeps the victim this far in front
		public const float MoveStepSpeed = 1.5f;       // m/s she steps in to keep that distance (StompFootReach otherwise)
		public const float StruggleNeeded = 12f;       // jump presses to break free...
		public const float StruggleDecay = 2f;         // ...minus this many per second of not pressing
		public const float BreakFreeCooldown = 3f;     // she can't start again for this long after an escape
		public const float PinBreakDistance = 1.2f;    // pin ends if the victim somehow ends up this far away
		public const string GrindSound = "gore_limb_3s";
		public const float GrindShake = 12f;           // camera shake per landing for the victim
		public const float GrindFlattenFirst = 0.25f;  // victim model squashed this much when pinned...
		public const float GrindFlattenStep = 0.06f;   // ...and this much more per landing
		public const float GrindMinFlatten = 0.3f;     // never flatter than this
		public const float GrindBloodScale = 0.5f;      // blood squirted per landing, relative to the squash spurts
		public const float GrindBloodMinScale = 0.4f;  // sized as if the victim were at least this big
		public const int GrindBloodBursts = 5;         // spurts around the sole per landing (+3 for a pinning stomp / kick)
		public static readonly bool GrindScreenBlood = true;   // the victim also gets a spurt right in front of their camera
		public const float GrindScreenBloodScale = 0.8f;
		public const float GrindTwistAngle = 30f;      // Rub: the foot pivots on its ball this far either way
		public const float ToeGrindTwistAngle = 35f;   // ToeGrind: the same, up on the toes

		// Procedural leg animation (client side, see GtsMoveAnim)
		public const float StompLeanBack = 8f;         // upper body leaning back while a knee is up
		public const float StompLeanForward = 15f;     // and pressing forward into a stomp
		public const float StompAxisSign = 1f;         // flip to -1 if the upper body bends the wrong way
		public const float LegReach = 0.92f;           // a leg never straightens past this fraction of its length...
		public const float MaxHipShift = 0.3f;         // ...the hips move toward the foot instead, up to this far
		public const float FootSplay = 8f;             // toes turned out, degrees
		public const float KneeOut = 0.15f;            // knees point slightly outward

		// Per-player opt-in: 1 = zombies can squash me too. Set by "gts zombiestomp on", read by the server.
		public const string ZombieStompCVar = "gtsZombieStomp";

		// Blood pool
		public static readonly string[] BloodBlocks = { "bloodDecor01", "bloodDecor02", "bloodDecor05", "bloodDecor06" };
		public const float BloodLifetime = 300f;   // seconds before the blood blocks are cleaned up

		// Squash FX (client side, see GtsGore)
		public static readonly string[] SquashSounds = { "gib", "gore_head" };
		public const float GoreParticleScale = 2.5f;   // size multiplier for the blood particle effects
		public static readonly bool FlatCorpse = false; // leave the flattened body in the blood pool
		public const float FlatCorpseThickness = 0.06f; // flattened corpse depth relative to the body
		public const float FlatCorpseLift = 0.02f;      // metres above the ground, to sit on top of the blood
		public const float GoreEatBloodScale = 1.5f;   // extra size for the blood_eat spurts on top of GoreParticleScale
		public const float BloodSplatScale = 1f;       // ground blood splat size (not tied to the victim size)
		public const float BloodSplatLift = 0.01f;     // metres above the ground
		public static readonly string BloodSplatVariant = "02"; // "01", "02", "05" or "06"; empty = random
		public static readonly bool UseBloodBlocks = false; // old blood decal blocks placed by the server
		public const float GoreMinScale = 0.1f;       // gore never shrinks below this, even for tinier victims
		public const int GoreMeatChunks = 6;
		public const int GoreLimbChunks = 3;
		public static readonly bool GoreSkull = true;
		public const float GoreChunkScale = 1f;
		public const float GoreChunkLifetime = 12f;    // seconds before flung chunks disappear
		public const float GoreFlingMin = 2f;          // horizontal launch speed range, m/s
		public const float GoreFlingMax = 5f;
		public const float GoreFlingUpMin = 3f;        // vertical launch speed range, m/s
		public const float GoreFlingUpMax = 6f;

		// Death camera when you are the one squashed
		public static readonly bool DeathCamFreeze = true;  // true: camera stays exactly where it was; false: wide shot below
		public const float DeathCamBack = 3f;      // metres behind where you were facing
		public const float DeathCamUp = 2f;        // metres above the squash spot
		public const float DeathCamHold = 3f;      // real seconds before the usual death fade starts
		public const float SlowMoScale = 0.3f;     // single player only; 1 = off
		public const float SlowMoDuration = 1f;    // real seconds

		public static float GetScale(EntityAlive _entity)
		{
			float s = _entity.Buffs.GetCustomVar(ScaleCVar);
			if (s <= 0f)
			{
				s = DefaultScale;
			}
			return Mathf.Clamp(s, MinScale, MaxScale);
		}

		public static bool IsSpeedAuto(EntityAlive _entity)
		{
			return _entity.Buffs.GetCustomVar(SpeedCVar) <= 0f;
		}

		public static float GetSpeedMult(EntityAlive _entity)
		{
			float m = _entity.Buffs.GetCustomVar(SpeedCVar);
			return m <= 0f ? GetScale(_entity) : Mathf.Clamp(m, MinSpeed, MaxSpeed);
		}

		public static bool ZombiesCanStomp(EntityAlive _victim)
		{
			return _victim.Buffs.GetCustomVar(ZombieStompCVar) > 0f;
		}

		// Has the shrink state at all, dead or alive (the buff survives death). Drives model and collider size,
		// so a corpse stays tiny and nothing pops back to full size between death and respawn.
		public static bool HasShrinkBuff(EntityAlive _entity)
		{
			return _entity != null && _entity.Buffs != null && _entity.Buffs.HasBuff(ShrinkBuff);
		}

		// Alive and shrunk: what stomping, camera, aiming and movement care about.
		public static bool IsShrunk(EntityAlive _entity)
		{
			return HasShrinkBuff(_entity) && !_entity.IsDead();
		}
	}
}
