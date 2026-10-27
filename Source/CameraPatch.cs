using HarmonyLib;
using UnityEngine;

namespace GTS
{
	// Lower the local camera toward the feet while shrunk, so the world looks huge. While pinned on their back
	// (GtsDownPose) a first-person camera lies down with them at head height and looks up at her.
	[HarmonyPatch(typeof(vp_FPCamera), nameof(vp_FPCamera.LateUpdate))]
	public static class CameraPatch
	{
		private static Vector3 lastOut;

		public static void Postfix(vp_FPCamera __instance)
		{
			vp_FPController fpc = __instance.FPController;
			EntityPlayerLocal player = fpc != null ? fpc.localPlayer : null;
			if (player == null || player.AttachedToEntity != null || !GtsConfig.IsShrunk(player))
			{
				return;
			}
			Transform t = __instance.transform;
			// LateUpdate didn't reposition the camera this frame (e.g. controller disabled); scaling again would compound.
			if (t.position == lastOut)
			{
				return;
			}
			// Scale around the same interpolated position the camera itself is built from;
			// using the entity position here makes the camera jitter while moving fast (fly mode).
			Vector3 feet = fpc.SmoothPosition;
			float scale = GtsConfig.GetScale(player);
			t.position = feet + (t.position - feet) * scale;
			if (player.bFirstPersonView && player.IsCameraAttachedToPlayerOrScope())
			{
				LieDown(player, t, feet, scale);
			}
			lastOut = t.position;
		}

		private const float EyeLying = 0.12f;   // eye above the ground lying down, full size
		private const float HeadBack = 0.35f;   // head this far from the victim's centre, away from her, full size
		private const float LookAtHeight = 0.6f; // look at her leg this high (her size, not scaled)

		private static void LieDown(EntityPlayerLocal _player, Transform _cam, Vector3 _feet, float _scale)
		{
			if (!GtsDownPose.Get(_player.entityId, out float k, out Vector3 toZombie, out int zombieId) || k <= 0f)
			{
				return;
			}
			Vector3 eye = _feet + Vector3.up * (EyeLying * _scale) - toZombie * (HeadBack * _scale);
			Vector3 look = Vector3.up * 0.6f + toZombie;   // no zombie: up and toward the feet
			World world = GameManager.Instance != null ? GameManager.Instance.World : null;
			if (world != null && world.GetEntity(zombieId) is EntityAlive z)
			{
				Vector3 at = z.position - Origin.position + Vector3.up * LookAtHeight;
				if ((at - eye).sqrMagnitude > 0.0001f)
				{
					look = at - eye;
				}
			}
			// lying on the back: the top of the view is past the top of the head
			Quaternion rot = Quaternion.LookRotation(look.normalized, -toZombie + Vector3.up * 0.01f);
			_cam.position = Vector3.Lerp(_cam.position, eye, k);
			_cam.rotation = Quaternion.Slerp(_cam.rotation, rot, k);
		}
	}
}
