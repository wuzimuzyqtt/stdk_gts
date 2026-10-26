using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GTS
{
	// How zombies treat a tiny player: female zombies squash them (if the player opted in with
	// "gts zombiestomp on"), male zombies that touch them fall apart instead.
	public static class GtsZombies
	{
		// Body parts cut off in order; the head goes last and is fatal.
		private static readonly EnumBodyPartHit[] DismemberOrder =
		{
			EnumBodyPartHit.LeftUpperArm, EnumBodyPartHit.RightUpperArm,
			EnumBodyPartHit.LeftUpperLeg, EnumBodyPartHit.RightUpperLeg,
			EnumBodyPartHit.Head
		};

		private static readonly Dictionary<int, bool> femaleByClass = new Dictionary<int, bool>();

		// Set while Dismember() is dealing its hits, so the CheckDismember patch forces the limb off.
		public static bool ForceDismember;

		// There is no gender tag on zombie classes (they all extend zombieTemplateMale), but every female zombie
		// uses a "...female..." hurt sound, so go by that.
		public static bool IsFemale(EntityZombie _zombie)
		{
			if (!femaleByClass.TryGetValue(_zombie.entityClass, out bool female))
			{
				female = _zombie.soundHurt != null && _zombie.soundHurt.ToLowerInvariant().Contains("female");
				femaleByClass[_zombie.entityClass] = female;
			}
			return female;
		}

		// Server only. Goes through the normal damage pipeline, so every client gets the usual dismemberment
		// gore through NetPackageDamageEntity.
		public static void Dismember(EntityZombie _zombie, Vector3 _from)
		{
			Vector3 dir = _zombie.position - _from;
			dir.y = 0f;
			dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
			ForceDismember = true;
			try
			{
				foreach (EnumBodyPartHit part in DismemberOrder)
				{
					if (_zombie.IsDead())
					{
						break;
					}
					DamageSource src = new DamageSource(EnumDamageSource.External, EnumDamageTypes.Slashing, dir);
					src.bodyParts = part;
					_zombie.DamageEntity(src, part == EnumBodyPartHit.Head ? 99999 : 1, false, 1f);
				}
			}
			finally
			{
				ForceDismember = false;
			}
		}
	}

	[HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.CheckDismember))]
	public static class ForceDismemberPatch
	{
		public static void Postfix(ref DamageResponse _dmResponse)
		{
			if (GtsZombies.ForceDismember)
			{
				_dmResponse.Dismember = true;
			}
		}
	}
}
