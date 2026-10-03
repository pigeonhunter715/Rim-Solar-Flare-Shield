using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SolarFlareShield;

[StaticConstructorOnStartup]
public static class Bootstrap
{
    private static bool rtPowerNetPatched;

    static Bootstrap()
    {
        var harmony = new Harmony("ai_for_rimworld.solarflareshield");
        HarmonyMethod postfix = new HarmonyMethod(typeof(Patches), nameof(Patches.AllowElectricityWithShield));
        harmony.Patch(
            AccessTools.Method(typeof(GameConditionManager), nameof(GameConditionManager.ElectricityDisabled)),
            postfix: postfix);

        PatchRTPowerNet(harmony, postfix);
        LongEventHandler.ExecuteWhenFinished(() => PatchRTPowerNet(harmony, postfix));

        Log.Message("[Solar Flare Shield] Solar flares remain active; shield buildings protect their map from the blackout.");
    }

    private static void PatchRTPowerNet(Harmony harmony, HarmonyMethod postfix)
    {
        if (rtPowerNetPatched)
        {
            return;
        }

        Type? rtPowerNetPatch = AccessTools.TypeByName("RT_SolarFlareShield.Patch_PowerNetTick");
        MethodInfo? rtElectricityDisabled = rtPowerNetPatch == null
            ? null
            : AccessTools.Method(rtPowerNetPatch, nameof(GameConditionManager.ElectricityDisabled));
        if (rtElectricityDisabled == null)
        {
            Log.Warning("[Solar Flare Shield] RT power check method was not found; vanilla electricity check remains patched.");
            return;
        }

        harmony.Patch(rtElectricityDisabled, postfix: postfix);
        rtPowerNetPatched = true;
        Log.Message("[Solar Flare Shield] RT Solar Flare Shield power check compatibility enabled.");
    }
}

public static class Patches
{
    public static void AllowElectricityWithShield(Map map, ref bool __result)
    {
        if (!__result || map == null)
        {
            return;
        }

        ThingDef shieldDef = DefDatabase<ThingDef>.GetNamedSilentFail("SolarFlareShield");
        if (shieldDef == null)
        {
            return;
        }

        foreach (Building building in map.listerBuildings.AllBuildingsColonistOfDef(shieldDef))
        {
            if (BuildingIsShielding(building))
            {
                __result = false;
                return;
            }
        }

        foreach (Building building in map.listerBuildings.AllBuildingsNonColonistOfDef(shieldDef))
        {
            if (BuildingIsShielding(building))
            {
                __result = false;
                return;
            }
        }
    }

    private static bool BuildingIsShielding(Building building)
    {
        CompSolarFlareShield shield = building.GetComp<CompSolarFlareShield>();
        return shield != null && shield.CanShieldNow;
    }
}

public class CompProperties_SolarFlareShield : CompProperties
{
    public float shieldingPowerDrain = 15000f;
    public float rotatorSpeedActive = 20f;
    public float rotatorSpeedIdle = 0.5f;

    public CompProperties_SolarFlareShield()
    {
        compClass = typeof(CompSolarFlareShield);
    }
}

public class CompSolarFlareShield : ThingComp
{
    private CompPowerTrader powerTrader = null!;
    private bool shielding;
    private ThingComp wirelessAdapter = null!;
    private PropertyInfo wirelessModeProperty = null!;
    private PropertyInfo wirelessNetProperty = null!;
    private PropertyInfo wirelessResearchProperty = null!;
    private PropertyInfo wirelessUnreservedEnergyProperty = null!;
    private object wirelessNet = null!;
    private float rotatorAngle = Rand.Range(0f, 360f);

    private CompProperties_SolarFlareShield Properties => (CompProperties_SolarFlareShield)props;

    // This state is deliberately independent of CompPowerTrader.PowerOn. The
    // vanilla power system can turn PowerOn off while a solar flare is active;
    // the shield must be able to report protection so the wireless network can
    // remain available during that same condition.
    public bool IsShielding => shielding
        && parent.Spawned
        && FlickUtility.WantsToBeOn(parent)
        && !parent.IsBrokenDown();

    public bool CanShieldNow => IsShielding && HasPowerForCurrentLoad();

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        powerTrader = parent.GetComp<CompPowerTrader>();
        CacheWirelessAdapter();
        RefreshPowerState();
    }

    public override void CompTick()
    {
        // Rotation is driven only by real available power. A stale PowerOn flag
        // is not enough: an unconnected, overloaded, switched-off or broken
        // building must keep the current angle.
        if (powerTrader != null
            && powerTrader.PowerOn
            && !parent.IsBrokenDown()
            && HasPowerForCurrentLoad())
        {
            rotatorAngle += shielding ? Properties.rotatorSpeedActive : Properties.rotatorSpeedIdle;
        }

        if (Find.TickManager.TicksGame % 60 == 0)
        {
            RefreshPowerState();
        }
    }

    public override void PostDraw()
    {
        Matrix4x4 matrix = default;
        matrix.SetTRS(
            parent.DrawPos + Altitudes.AltIncVect,
            Quaternion.AngleAxis(rotatorAngle, Vector3.up),
            new Vector3(2f, 2f, 2f));
        Graphics.DrawMesh(MeshPool.plane10, matrix, Visuals.RotatorTexture, 0);
    }

    private void RefreshPowerState()
    {
        if (powerTrader == null || !parent.Spawned || parent.Map == null || parent.IsBrokenDown())
        {
            shielding = false;
            return;
        }

        CacheWirelessAdapter();
        bool wantsPower = FlickUtility.WantsToBeOn(parent);
        bool solarFlareActive = SolarFlareActive(parent.Map);
        float standbyWatts = powerTrader.Props.PowerConsumption;

        if (!wantsPower)
        {
            shielding = false;
            powerTrader.PowerOutput = -standbyWatts;
            return;
        }

        if (solarFlareActive)
        {
            powerTrader.PowerOutput = -Properties.shieldingPowerDrain;
            bool wirelessMode;
            bool activeAvailable = TryCanPayWithNivarianWireless(
                Properties.shieldingPowerDrain,
                out wirelessMode);

            if (!activeAvailable && !wirelessMode)
            {
                activeAvailable = CanPayLocalPower(Properties.shieldingPowerDrain);
            }

            if (activeAvailable)
            {
                powerTrader.PowerOn = true;
                shielding = true;
                return;
            }

            shielding = false;
            powerTrader.PowerOutput = -standbyWatts;
            return;
        }

        shielding = false;
        powerTrader.PowerOutput = -standbyWatts;
        // The regular power net controls PowerOn. For wireless buildings, only
        // enable standby when the remote network can really supply it.
        if (TryCanPayWithNivarianWireless(standbyWatts, out bool wirelessStandbyMode))
        {
            powerTrader.PowerOn = true;
        }
        else if (wirelessStandbyMode)
        {
            powerTrader.PowerOn = false;
        }
    }

    private bool HasPowerForCurrentLoad()
    {
        float watts = shielding ? Properties.shieldingPowerDrain : powerTrader.Props.PowerConsumption;
        if (TryCanPayWithNivarianWireless(watts, out bool wirelessMode))
        {
            return true;
        }

        if (wirelessMode)
        {
            return false;
        }

        return CanPayLocalPower(watts);
    }

    private bool CanPayLocalPower(float watts)
    {
        PowerNet powerNet = powerTrader.PowerNet;
        if (powerNet == null)
        {
            return false;
        }

        // CurrentEnergyGainRate already includes this building's current
        // standby draw. Add that draw back before comparing the requested
        // active load, otherwise a 15,000 W shield can reject a valid grid.
        float available = powerNet.CurrentEnergyGainRate()
            + powerNet.CurrentStoredEnergy();
        float required = watts * CompPower.WattsToWattDaysPerTick;
        return available >= required + powerTrader.EnergyOutputPerTick - 0.000001f;
    }

    private bool TryCanPayWithNivarianWireless(float watts, out bool wirelessMode)
    {
        wirelessMode = false;
        CacheWirelessAdapter();
        if (wirelessAdapter == null || wirelessModeProperty == null)
        {
            return false;
        }

        wirelessMode = wirelessModeProperty.GetValue(wirelessAdapter) is bool enabled && enabled;
        if (!wirelessMode)
        {
            return false;
        }

        if (wirelessNet == null || wirelessResearchProperty == null || wirelessUnreservedEnergyProperty == null)
        {
            return false;
        }

        bool researchComplete = wirelessResearchProperty.GetValue(wirelessNet) is bool complete && complete;
        if (!researchComplete)
        {
            return false;
        }

        float available = Convert.ToSingle(wirelessUnreservedEnergyProperty.GetValue(wirelessNet));
        float required = watts * CompPower.WattsToWattDaysPerTick;
        return available >= required - 0.000001f;
    }

    private void CacheWirelessAdapter()
    {
        if (parent.AllComps == null)
        {
            return;
        }

        if (wirelessAdapter == null)
        {
            foreach (ThingComp comp in parent.AllComps)
            {
                if (comp.GetType().FullName != "Nivarian_Race.Code.Comps.BuildingComps.Comp_NivarianWirelessPowerAdapter")
                {
                    continue;
                }

                wirelessAdapter = comp;
                wirelessModeProperty = comp.GetType().GetProperty(
                    "WirelessMode",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                wirelessNetProperty = comp.GetType().GetProperty(
                    "Net",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                break;
            }
        }

        if (wirelessAdapter == null || wirelessNetProperty == null)
        {
            return;
        }

        wirelessNet = wirelessNetProperty.GetValue(wirelessAdapter);
        if (wirelessNet == null)
        {
            return;
        }

        Type netType = wirelessNet.GetType();
        wirelessResearchProperty ??= netType.GetProperty(
            "IsWirelessResearchCompleted",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        wirelessUnreservedEnergyProperty ??= netType.GetProperty(
            "UnreservedEnergy",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    private static bool SolarFlareActive(Map map)
    {
        foreach (GameCondition condition in map.gameConditionManager.ActiveConditions)
        {
            if (condition?.def?.defName == "SolarFlare"
                && condition.CanApplyOnMap(map)
                && !condition.HiddenByOtherCondition(map))
            {
                return true;
            }
        }

        if (Find.World?.gameConditionManager != null)
        {
            foreach (GameCondition condition in Find.World.gameConditionManager.ActiveConditions)
            {
                if (condition?.def?.defName == "SolarFlare"
                    && condition.CanApplyOnMap(map)
                    && !condition.HiddenByOtherCondition(map))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

[StaticConstructorOnStartup]
public static class Visuals
{
    public static readonly Material RotatorTexture =
        MaterialPool.MatFrom("SolarFlareShield/Building_RTMagneticShield_Top", ShaderDatabase.Cutout);
}
