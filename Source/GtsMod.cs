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

		// Female zombie stomp: she starts lifting her foot within StompTriggerRange; when it comes down
		// (StompRaiseTime + StompHoldTime + StompSlamTime later) the server checks whether the victim is still
		// under it. Players squash instantly, no animation.
		public const float StompTriggerRange = 1.0f;   // horizontal metres, zombie to victim
		public const float StompFootReach = 0.35f;     // foot lands this far in front of the zombie
		public const float StompHitLeniency = 0.15f;   // extra radius around the landing spot
		public const float StompCooldown = 1.5f;       // seconds before the same zombie stomps again

		// Procedural stomp animation (client side, see GtsStompAnim)
		public const float StompRaiseTime = 0.35f;
		public const float StompHoldTime = 0.1f;
		public const float StompSlamTime = 0.1f;
		public const float StompPlantTime = 0.12f;     // foot stays planted on the victim after impact
		public const float StompAnimLength = 0.9f;     // including easing back into the normal animation
		public const float StompHipAngle = 75f;        // thigh raised forward
		public const float StompKneeAngle = 90f;       // knee bend at the top
		public const float StompSlamHipAngle = 30f;    // thigh still forward when the foot lands, so it lands in front
		public const float StompLeanBack = 8f;         // upper body leaning back while the knee is up
		public const float StompLeanForward = 15f;     // and pressing forward into the stomp
		public const float StompAxisSign = 1f;         // flip to -1 if the leg bends the wrong way

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
