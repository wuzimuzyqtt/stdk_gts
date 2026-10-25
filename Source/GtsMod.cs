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
		public const float StompRadius = 0.6f;     // horizontal distance between stomper and victim
		public const float StompMinDY = -0.4f;     // stomper feet relative to victim feet
		public const float StompMaxDY = 1.0f;
		public const float StompMinSpeed = 0.8f;   // m/s, standing still next to a tiny player is safe
		public static readonly bool ZombiesCanStomp = false;

		// Blood pool
		public static readonly string[] BloodBlocks = { "bloodDecor01", "bloodDecor02", "bloodDecor05", "bloodDecor06" };
		public const float BloodLifetime = 300f;   // seconds before the blood blocks are cleaned up

		// Squash FX (client side)
		public const string BloodParticle = "blood_death";
		public const int BloodBursts = 6;
		public static readonly string[] SquashSounds = { "gib", "gore_head" };

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

		public static bool IsShrunk(EntityAlive _entity)
		{
			return _entity != null && _entity.Buffs != null && !_entity.IsDead() && _entity.Buffs.HasBuff(ShrinkBuff);
		}
	}
}
