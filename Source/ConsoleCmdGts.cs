using System.Collections.Generic;
using UnityEngine;

namespace GTS
{
	public class ConsoleCmdGts : ConsoleCmdAbstract
	{
		public override bool IsExecuteOnClient => true;

		// everyone may use it, not just admins
		public override int DefaultPermissionLevel => 1000;

		public override string[] getCommands()
		{
			return new string[] { "gts" };
		}

		public override string getDescription()
		{
			return "GTS: shrink yourself / return to normal size";
		}

		public override string getHelp()
		{
			return "Usage:\n"
				+ "  gts shrink [scale]  - shrink yourself (default 0.25, range 0.01-0.9)\n"
				+ "  gts grow            - back to normal size (needs room above you)\n"
				+ "  gts speed <mult>    - movement speed while shrunk, e.g. 0.5 (range 0.01-3)\n"
				+ "  gts speed auto      - speed follows your size (default)\n"
				+ "  gts zombiestomp on|off - let female zombies squash you while shrunk (default off)\n"
				+ "  gts status          - show current state\n"
				+ "  gts gore            - preview the squash gore in front of you at your size (only you see it)\n"
				+ "  gts bloodtest       - lay the 4 blood pool variants in a row in front of you (1-4, left to right)\n"
				+ "  gts stomptest       - nearest zombie plays the stomp animation (only you see it)\n"
				+ "  gts animinfo        - log the nearest zombie's rig and animation clips (read only)\n"
				+ "  gts ray             - debug: list what a gunshot from your view would hit";
		}

		public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
		{
			World world = GameManager.Instance.World;
			EntityPlayerLocal player = world != null ? world.GetPrimaryPlayer() : null;
			if (player == null)
			{
				Out("gts: must be run from a game client while in game");
				return;
			}
			string sub = _params.Count > 0 ? _params[0].ToLowerInvariant() : "status";
			switch (sub)
			{
				case "shrink":
				{
					float scale = GtsConfig.DefaultScale;
					if (_params.Count > 1 && !StringParsers.TryParseFloat(_params[1], out scale))
					{
						Out("gts: invalid scale '" + _params[1] + "'");
						return;
					}
					scale = Mathf.Clamp(scale, GtsConfig.MinScale, GtsConfig.MaxScale);
					if (!CheckRoom(player, scale))
					{
						return;
					}
					player.Buffs.SetCustomVar(GtsConfig.ScaleCVar, scale);
					player.Buffs.AddBuff(GtsConfig.ShrinkBuff);
					Out("gts: shrunk to " + scale.ToString("0.###") + "x, speed " + SpeedText(player) + ". Don't get stepped on!");
					break;
				}
				case "grow":
					if (!CheckRoom(player, 1f))
					{
						return;
					}
					player.Buffs.RemoveBuff(GtsConfig.ShrinkBuff);
					Out("gts: back to normal size");
					break;
				case "speed":
				{
					if (_params.Count < 2)
					{
						Out("gts: speed " + SpeedText(player));
						return;
					}
					float mult = 0f;
					if (_params[1].ToLowerInvariant() != "auto")
					{
						if (!StringParsers.TryParseFloat(_params[1], out mult))
						{
							Out("gts: invalid speed '" + _params[1] + "'");
							return;
						}
						mult = Mathf.Clamp(mult, GtsConfig.MinSpeed, GtsConfig.MaxSpeed);
					}
					player.Buffs.SetCustomVar(GtsConfig.SpeedCVar, mult);
					Out("gts: speed " + SpeedText(player) + (GtsConfig.IsShrunk(player) ? "" : " (applies while shrunk)"));
					break;
				}
				case "zombiestomp":
				{
					if (_params.Count > 1)
					{
						string v = _params[1].ToLowerInvariant();
						if (v != "on" && v != "off")
						{
							Out("gts: use 'gts zombiestomp on' or 'gts zombiestomp off'");
							return;
						}
						player.Buffs.SetCustomVar(GtsConfig.ZombieStompCVar, v == "on" ? 1f : 0f);
					}
					Out("gts: female zombies " + (GtsConfig.ZombiesCanStomp(player) ? "CAN" : "cannot") + " squash you while shrunk (male zombies always fall apart on contact)");
					break;
				}
				case "status":
					Out(GtsConfig.HasShrinkBuff(player)
						? "gts: shrunk, scale " + GtsConfig.GetScale(player).ToString("0.###") + "x, speed " + SpeedText(player) + ZombieText(player)
						: "gts: normal size, speed setting " + SpeedText(player) + ZombieText(player));
					break;
				case "gore":
				{
					Vector3 fwd = player.GetForwardVector();
					fwd.y = 0f;
					float scale = GtsConfig.HasShrinkBuff(player) ? GtsConfig.GetScale(player) : 1f;
					Vector3 spot = GtsGround.Find(player.position + fwd.normalized * 2f * Mathf.Max(scale, 0.25f), out _);
					GtsGore.Play(spot, scale);
					GtsGore.Splat(spot);
					if (GtsConfig.FlatCorpse)
					{
						GtsCorpse.Spawn(player, spot, scale);
					}
					Out("gts: gore preview played");
					break;
				}
				case "bloodtest":
				{
					Vector3 fwd = player.GetForwardVector();
					fwd.y = 0f;
					fwd.Normalize();
					Vector3 right = Vector3.Cross(Vector3.up, fwd);
					Vector3 start = player.position + fwd * 2.5f - right * 1.8f;
					for (int i = 0; i < GtsGore.SplatVariants.Length; i++)
					{
						GtsGore.Splat(start + right * (1.2f * i), GtsGore.SplatVariants[i], 120f);
					}
					Out("gts: blood pools 1-4 (left to right) = " + string.Join(", ", GtsGore.SplatVariants) + ", gone in 2 minutes");
					break;
				}
				case "stomptest":
				{
					EntityZombie z = NearestZombie(world, player, 20f);
					if (z == null)
					{
						Out("gts: no zombie within 20m");
						return;
					}
					GtsStompAnim.Play(z);
					Out("gts: " + z.GetDebugName() + " stomps");
					break;
				}
				case "animinfo":
				{
					EntityZombie z = NearestZombie(world, player, 20f);
					Animator anim = z != null && z.emodel != null && z.emodel.avatarController != null ? z.emodel.avatarController.GetAnimator() : null;
					if (anim == null)
					{
						Out("gts: no zombie with an animator within 20m");
						return;
					}
					RuntimeAnimatorController rac = anim.runtimeAnimatorController;
					Out("gts: " + z.GetDebugName() + " humanoid=" + anim.isHuman + " avatar=" + (anim.avatar != null ? anim.avatar.name : "none")
						+ " controller=" + (rac != null ? rac.name : "none"));
					if (rac != null)
					{
						AnimationClip[] clips = rac.animationClips;
						Out("gts: " + clips.Length + " clips (full list in the log)");
						foreach (AnimationClip c in clips)
						{
							Log.Out("[GTS] clip: " + c.name + " (" + c.length.ToString("0.00") + "s)");
						}
					}
					break;
				}
				case "ray":
					DebugRay(player);
					break;
				default:
					Out(getHelp());
					break;
			}
		}

		private static EntityZombie NearestZombie(World _world, EntityPlayer _player, float _maxDist)
		{
			EntityZombie best = null;
			float bestSq = _maxDist * _maxDist;
			foreach (Entity e in _world.Entities.list)
			{
				if (e is EntityZombie z && !z.IsDead())
				{
					float sq = (z.position - _player.position).sqrMagnitude;
					if (sq < bestSq)
					{
						bestSq = sq;
						best = z;
					}
				}
			}
			return best;
		}

		private static bool CheckRoom(EntityPlayerLocal _player, float _newScale)
		{
			if (GtsColliders.CanGrow(_player, _newScale, out string blocker))
			{
				return true;
			}
			Out("gts: not enough room to grow here (blocked by " + blocker + "). Move to an open spot first.");
			return false;
		}

		private static string ZombieText(EntityPlayerLocal _player)
		{
			return ", zombie stomp " + (GtsConfig.ZombiesCanStomp(_player) ? "on" : "off");
		}

		private static string SpeedText(EntityPlayerLocal _player)
		{
			return GtsConfig.IsSpeedAuto(_player)
				? "auto (" + GtsConfig.GetSpeedMult(_player).ToString("0.###") + "x)"
				: GtsConfig.GetSpeedMult(_player).ToString("0.###") + "x";
		}

		private static void DebugRay(EntityPlayerLocal _player)
		{
			int modelLayer = _player.GetModelLayer();
			_player.SetModelLayer(2);   // the shot hides the shooter's own model the same way
			try
			{
				GtsDebug.TraceShot(_player, _player.GetLookRay(), Out);
			}
			finally
			{
				_player.SetModelLayer(modelLayer);
			}
		}

		private static void Out(string _line)
		{
			SingletonMonoBehaviour<SdtdConsole>.Instance.Output(_line);
		}
	}
}
