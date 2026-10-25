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
				+ "  gts status          - show current state\n"
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
				case "status":
					Out(GtsConfig.IsShrunk(player)
						? "gts: shrunk, scale " + GtsConfig.GetScale(player).ToString("0.###") + "x, speed " + SpeedText(player)
						: "gts: normal size, speed setting " + SpeedText(player));
					break;
				case "ray":
					DebugRay(player);
					break;
				default:
					Out(getHelp());
					break;
			}
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
