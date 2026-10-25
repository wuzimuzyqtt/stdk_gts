#!/bin/sh
# Build GTS.dll into the mod folder. Uses the Roslyn compiler that ships with VS 2022 Build Tools.
set -e
cd "$(dirname "$0")"
GAME="../../.."
M="$GAME/7DaysToDie_Data/Managed"
CSC="/c/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/MSBuild/Current/Bin/Roslyn/csc.exe"
"$CSC" -nologo -target:library -optimize+ -nostdlib+ -noconfig -langversion:9 -nowarn:1701,1702 \
  -out:../GTS.dll \
  -r:"$M/mscorlib.dll" -r:"$M/System.dll" -r:"$M/System.Core.dll" -r:"$M/netstandard.dll" \
  -r:"$M/Assembly-CSharp.dll" -r:"$M/Assembly-CSharp-firstpass.dll" -r:"$M/LogLibrary.dll" \
  -r:"$M/UnityEngine.dll" -r:"$M/UnityEngine.CoreModule.dll" -r:"$M/UnityEngine.PhysicsModule.dll" \
  -r:"$GAME/Mods/0_TFP_Harmony/0Harmony.dll" \
  *.cs
echo "Built ../GTS.dll"
