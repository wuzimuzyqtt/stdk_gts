using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace GTS
{
	// When the local player is squashed, the vanilla death camera starts at the (tiny, ground level) eye position
	// and creeps back while the screen fades, so the squash happens in your face and is gone before you see it.
	// Instead either freeze the camera exactly where it was (default; keeps a detached "P" camera shot too) or
	// jump to a wide shot of the spot, hold it a few seconds before fading, and (single player only) slow time.
	[HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.startDeathCamera))]
	public static class DeathCamPatch
	{
		// Set while our camera owns the death view of the local player.
		public static bool Active;
		public static Vector3 Spot;      // world position of the squash
		public static Vector3 CamPos;    // world position we want the camera at
		public static Quaternion CamRot; // frozen mode: camera rotation at the moment of death

		// Grab the camera before startDeathCamera switches the view and starts moving it.
		public static void Prefix(EntityPlayerLocal __instance)
		{
			Transform cam = __instance.cameraTransform;
			CamPos = cam.position + Origin.position;
			CamRot = cam.rotation;
		}

		public static void Postfix(EntityPlayerLocal __instance)
		{
			Active = false;
			if (!GtsDriver.Squashed.ContainsKey(__instance.entityId))
			{
				return;
			}
			Spot = __instance.position;
			if (!GtsConfig.DeathCamFreeze)
			{
				Vector3 fwd = __instance.GetForwardVector();
				fwd.y = 0f;
				fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
				CamPos = Spot - fwd * GtsConfig.DeathCamBack + Vector3.up * GtsConfig.DeathCamUp;
			}
			Active = true;

			ScreenEffects fx = __instance.ScreenEffectManager;
			fx.SetScreenEffect("Dying", 0f, 0f);
			fx.SetScreenEffect("Dead", 0f, 0f);
			fx.SetScreenEffect("FadeToBlack", 0f, 0f);

			if (GtsDriver.Instance != null)
			{
				GtsDriver.Instance.StartCoroutine(DelayedFade(__instance));
				if (CanSlowTime(__instance))
				{
					GtsDriver.Instance.StartCoroutine(SlowMotion());
				}
			}
		}

		private static IEnumerator DelayedFade(EntityPlayerLocal _player)
		{
			yield return new WaitForSecondsRealtime(GtsConfig.DeathCamHold);
			if (_player == null || !_player.IsDead())
			{
				yield break;
			}
			ScreenEffects fx = _player.ScreenEffectManager;
			fx.SetScreenEffect("Dying", 0.5f, 0.5f);
			fx.SetScreenEffect("Dead", 1f, 0.5f);
			fx.SetScreenEffect("FadeToBlack", 1f, _player.GetTimeStayAfterDeath() * 0.05f);
		}

		// Time.timeScale is global; on a server it would slow everyone down, so only when playing alone.
		private static bool CanSlowTime(EntityPlayerLocal _player)
		{
			return GtsConfig.SlowMoScale < 1f
				&& SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer
				&& _player.world.Players.list.Count == 1
				&& Time.timeScale == 1f;
		}

		private static IEnumerator SlowMotion()
		{
			Time.timeScale = GtsConfig.SlowMoScale;
			yield return new WaitForSecondsRealtime(GtsConfig.SlowMoDuration);
			// don't fight the pause menu (timeScale 0) or anything else that changed it meanwhile
			if (Time.timeScale == GtsConfig.SlowMoScale)
			{
				Time.timeScale = 1f;
			}
		}
	}

	// The vanilla per-frame death camera aims at the body's chest and pulls the camera in front of anything a
	// 0.28m sphere cast from there hits. A squashed body is shrunk to nothing, so its "chest" is 0.2m above the
	// ground, the sphere starts inside the ground, and the camera gets yanked down onto the floor every frame.
	// While our squash camera is active, run our own update instead.
	[HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.SelfCameraFrameUpdate))]
	public static class DeathCamFramePatch
	{
		private const int BlockMask = 65536;   // same mask the vanilla death camera collides with
		private const float LookHeight = 0.3f;
		private const float CastStartHeight = 0.5f;
		private const float WallPadding = 0.2f;

		public static bool Prefix(EntityPlayerLocal __instance)
		{
			if (!DeathCamPatch.Active)
			{
				return true;
			}
			if (!__instance.IsDead())
			{
				DeathCamPatch.Active = false;
				return true;
			}
			Transform cam = __instance.cameraTransform;
			if (GtsConfig.DeathCamFreeze)
			{
				cam.position = DeathCamPatch.CamPos - Origin.position;
				cam.rotation = DeathCamPatch.CamRot;
				__instance.selfCameraPos = DeathCamPatch.CamPos;
				__instance.selfCameraSeekPos = DeathCamPatch.CamPos;
				return false;
			}
			Vector3 look = DeathCamPatch.Spot + Vector3.up * LookHeight - Origin.position;
			Vector3 start = DeathCamPatch.Spot + Vector3.up * CastStartHeight - Origin.position;
			Vector3 want = DeathCamPatch.CamPos - Origin.position;
			Vector3 dir = want - start;
			float dist = dir.magnitude;
			if (dist > 0.01f && Physics.Raycast(start, dir, out RaycastHit hit, dist, BlockMask))
			{
				want = start + dir.normalized * Mathf.Max(0.05f, hit.distance - WallPadding);
			}
			cam.position = want;
			cam.rotation = Quaternion.Slerp(cam.rotation, Quaternion.LookRotation(look - want), 0.15f);
			__instance.selfCameraPos = want + Origin.position;
			__instance.selfCameraSeekPos = __instance.selfCameraPos;
			return false;
		}
	}
}
