using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using HarmonyLib;
using UnityEngine.Profiling;
using System.Diagnostics.Eventing.Reader;

namespace PowerSystemTweek
{
    public enum EExchangerDischargeStrategy
    {
        EQUAL_AS_FUEL_GENERATOR = 0,
        DISCHARGE_FIRST = 1,
        DISCHARGE_LAST = 2
    }

    public class PowerSystemPatch
    {
        public static EExchangerDischargeStrategy ExchangerDischargeStrategy = EExchangerDischargeStrategy.EQUAL_AS_FUEL_GENERATOR;
        public static bool PowerSystemGameTickPatch = true;
        public static double[] ExecuteTime = new double[512];
        public static List<HighStopwatch> StopWatchList = new List<HighStopwatch>(512);
        public static void InitStopWatch()
        {
            for(int index = 0; index < 512; index++)
            {
                StopWatchList.Add(new HighStopwatch());
            }
        }

        public static void SetExchangerDischargePriority(EExchangerDischargeStrategy strategy)
        {
            ExchangerDischargeStrategy = strategy;
        }
        //[HarmonyPrefix, HarmonyPatch(typeof(PowerGeneratorComponent), nameof(PowerGeneratorComponent.EnergyCap_Gamma_Req))]
        //static bool EnergyCap_Gamma_Req_Prefix(PowerGeneratorComponent __instance, float sx, float sy, float sz, float increase, float eta, ref long __result)
        //{
        //    if (GameMain.gameTick % 60 == __instance.id)
        //    {
        //        float num_power_gen_cap = (float)(((double)sx * (double)__instance.x + (double)sy * (double)__instance.y + (double)sz * (double)__instance.z + (double)increase * 0.800000011920929 + (__instance.catalystPoint > 0 ? (double)__instance.ionEnhance : 0.0)) * 6.0 + 0.5);
        //        float num_cons_req = (double)num_power_gen_cap > 1.0 ? 1f : ((double)num_power_gen_cap < 0.0 ? 0.0f : num_power_gen_cap);
        //        __instance.currentStrength = num_cons_req;
        //        float num_power_discharge = (float)Cargo.accTableMilli[__instance.catalystIncLevel];
        //        __instance.capacityCurrentTick = (long)((double)__instance.currentStrength * (1.0 + (double)__instance.warmup * 1.5) * (__instance.catalystPoint > 0 ? 2.0 * (1.0 + (double)num_power_discharge) : 1.0) * (__instance.productId > 0 ? 8.0 : 1.0) * (double)__instance.genEnergyPerTick);
        //        __instance.warmupSpeed = (float)(((double)num_cons_req - 0.75) * 4.0 * 1.38888890433009E-05);                
        //    }

        //    eta = (float)(1.0 - (1.0 - (double)eta) * (1.0 - (double)__instance.warmup * (double)__instance.warmup * 0.400000005960464));
        //    __result = (long)((double)__instance.capacityCurrentTick / (double)eta + 0.49999999);
        //    return false;
        //}

        [HarmonyPrefix, HarmonyPatch(typeof(PowerSystem), nameof(PowerSystem.CalculatePowerSystemWeight))]
        static bool CalculatePowerSystemWeightPrefix(PowerSystem __instance, ref int ___totalPowerSystemWeight)
        {
            if (!PowerSystemGameTickPatch)
            {
                return true;
            }
            else {
                ___totalPowerSystemWeight = 10 + (int)(100000000.0 * ExecuteTime[__instance.planet.factoryIndex]);
                return false;
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PowerSystem), nameof(PowerSystem.GameTick))]
        static bool GameTickPrefixExperimental(PowerSystem __instance, long time, bool isActive, bool multithreaded = false)
        {
            if (!PowerSystemGameTickPatch)
            {
                return true;
            }
            var powerSystem = __instance;
            StopWatchList[powerSystem.factory.index].Begin();
            DysonSphere dysonSphere = powerSystem.factory.dysonSphere;

            FactoryProductionStat factoryProductionStat = GameMain.statistics.production.factoryStatPool[powerSystem.factory.index];
            int[] productRegister = factoryProductionStat.productRegister;
            int[] consumeRegister = factoryProductionStat.consumeRegister;
            long num_power_gen_cap = 0;
            long num_cons_req = 0;
            long num_power_discharge = 0;
            long num_power_charge = 0;
            long num_energy_cons_stat = 0;
            float _dt = 0.0166666675f;
            PlanetData planet = powerSystem.factory.planet;
            float windStrength = planet.windStrength;
            float luminosity = planet.luminosity;
            Vector3 normalized = planet.runtimeLocalSunDirection.normalized;
            AnimData[] entityAnimPool = powerSystem.factory.entityAnimPool;
            SignData[] entitySignPool = powerSystem.factory.entitySignPool;
            if (powerSystem.networkServes == null || powerSystem.networkServes.Length != powerSystem.netPool.Length)
                powerSystem.networkServes = new float[powerSystem.netPool.Length];
            if (powerSystem.networkGenerates == null || powerSystem.networkGenerates.Length != powerSystem.netPool.Length)
                powerSystem.networkGenerates = new float[powerSystem.netPool.Length];
            bool useIonLayer = GameMain.history.useIonLayer;
            bool useCata = time % 10L == 0L;
            Array.Clear((Array)powerSystem.currentGeneratorCapacities, 0, powerSystem.currentGeneratorCapacities.Length);
            Player mainPlayer = GameMain.mainPlayer;
            Vector3 zero = Vector3.zero;
            Vector3 vector3_1 = Vector3.zero;
            float num6 = 0.0f;
            bool flag1;
            if (mainPlayer.mecha.coreEnergyCap - mainPlayer.mecha.coreEnergy > 0.0 && mainPlayer.isAlive && mainPlayer.planetId == planet.id)
            {
                float num7 = powerSystem.factory.planet.realRadius + 0.2f;
                Vector3 vector3_2 = multithreaded ? powerSystem.multithreadPlayerPos : mainPlayer.position;
                float magnitude = vector3_2.magnitude;
                if ((double)magnitude > 0.0)
                    vector3_1 = vector3_2 * (num7 / magnitude);
                flag1 = (double)magnitude > (double)num7 - 30.0 && (double)magnitude < (double)num7 + 50.0;
            }
            else
                flag1 = false;
            lock (mainPlayer.mecha)
                num6 = Mathf.Pow(Mathf.Clamp01((float)(1.0 - mainPlayer.mecha.coreEnergy / mainPlayer.mecha.coreEnergyCap) * 10f), 0.75f);
            /** Patch note, replace private field dysonSphere with traversed local variable **/
            float response = dysonSphere != null ? dysonSphere.energyRespCoef : 0.0f;
            int solar_decimation = (int)(Math.Min(Math.Abs(powerSystem.factory.planet.rotationPeriod), Math.Abs(powerSystem.factory.planet.orbitalPeriod)) * 60.0 / 2160.0);
            if (solar_decimation < 1)
                solar_decimation = 1;
            else if (solar_decimation > 60)
                solar_decimation = 60;
            if (powerSystem.factory.planet.singularity == EPlanetSingularity.TidalLocked)
                solar_decimation = 60;
            bool flag2 = time % (long)solar_decimation == 0L || GameMain.onceGameTick <= 2L;
            int num9 = (int)(time % 90L);
            EntityData[] entityPool = powerSystem.factory.entityPool;
            for (int index1 = 1; index1 < powerSystem.netCursor; ++index1)
            {
                PowerNetwork powerNetwork = powerSystem.netPool[index1];
                if (powerNetwork != null && powerNetwork.id == index1)
                {
                    List<int> consumers = powerNetwork.consumers;
                    int count1 = consumers.Count;
                    long num_energy_req = 0;
                    for (int index2 = 0; index2 < count1; ++index2)
                    {
                        long requiredEnergy = powerSystem.consumerPool[consumers[index2]].requiredEnergy;
                        num_energy_req += requiredEnergy;
                        num_cons_req += requiredEnergy;
                    }
                    foreach (PowerNetworkStructures.Node node in powerNetwork.nodes)
                    {
                        int id = node.id;
                        if (powerSystem.nodePool[id].id == id && powerSystem.nodePool[id].isCharger)
                        {
                            if ((double)powerSystem.nodePool[id].coverRadius <= 20.0)
                            {
                                double num11 = 0.0;
                                if (flag1)
                                {
                                    double num12 = (double)powerSystem.nodePool[id].powerPoint.x * 0.98799997568130493 - (double)vector3_1.x;
                                    float num13 = powerSystem.nodePool[id].powerPoint.y * 0.988f - vector3_1.y;
                                    float num14 = powerSystem.nodePool[id].powerPoint.z * 0.988f - vector3_1.z;
                                    float coverRadius = powerSystem.nodePool[id].coverRadius;
                                    if ((double)coverRadius < 9.0)
                                        coverRadius += 2.01f;
                                    else if ((double)coverRadius > 20.0)
                                        coverRadius += 0.5f;
                                    float num15 = (float)(num12 * num12 + (double)num13 * (double)num13 + (double)num14 * (double)num14);
                                    float num16 = coverRadius * coverRadius;
                                    if ((double)num15 <= (double)num16)
                                    {
                                        double consumerRatio = powerNetwork.consumerRatio;
                                        float num17 = (float)(((double)num16 - (double)num15) / (3.0 * (double)coverRadius));
                                        if ((double)num17 > 1.0)
                                            num17 = 1f;
                                        num11 = (double)num6 * consumerRatio * consumerRatio * (double)num17;
                                    }
                                }
                                double num18 = (double)powerSystem.nodePool[id].idleEnergyPerTick * (1.0 - num11) + (double)powerSystem.nodePool[id].workEnergyPerTick * num11;
                                if (powerSystem.nodePool[id].requiredEnergy < powerSystem.nodePool[id].idleEnergyPerTick)
                                    powerSystem.nodePool[id].requiredEnergy = powerSystem.nodePool[id].idleEnergyPerTick;
                                if ((double)powerSystem.nodePool[id].requiredEnergy < num18 - 0.01)
                                {
                                    double num19 = num18 * 0.02 + (double)powerSystem.nodePool[id].requiredEnergy * 0.98;
                                    powerSystem.nodePool[id].requiredEnergy = (int)(num19 + 0.9999);
                                }
                                else if ((double)powerSystem.nodePool[id].requiredEnergy > num18 + 0.01)
                                {
                                    double num20 = num18 * 0.2 + (double)powerSystem.nodePool[id].requiredEnergy * 0.8;
                                    powerSystem.nodePool[id].requiredEnergy = (int)num20;
                                }
                            }
                            else
                                powerSystem.nodePool[id].requiredEnergy = powerSystem.nodePool[id].idleEnergyPerTick;
                            long requiredEnergy = (long)powerSystem.nodePool[id].requiredEnergy;
                            num_energy_req += requiredEnergy;
                            num_cons_req += requiredEnergy;
                        }
                    }
                    long num_gen_exc_output_cap = 0;
                    List<int> exchangers = powerNetwork.exchangers;
                    int count2 = exchangers.Count;
                    long num_exc_charged_total = 0;
                    long num_exc_discharged_total = 0;
                    long num_exc_output_cap = 0;
                    long num_exc_input_cap = 0;
                    for (int index3 = 0; index3 < count2; ++index3)
                    {
                        int index4 = exchangers[index3];
                        powerSystem.excPool[index4].StateUpdate();
                        powerSystem.excPool[index4].BeltUpdate(powerSystem.factory);
                        bool flag3 = (double)powerSystem.excPool[index4].state >= 1.0;
                        bool flag4 = (double)powerSystem.excPool[index4].state <= -1.0;
                        if (!flag3 && !flag4)
                        {
                            powerSystem.excPool[index4].capsCurrentTick = 0L;
                            powerSystem.excPool[index4].currEnergyPerTick = 0L;
                        }
                        int entityId = powerSystem.excPool[index4].entityId;
                        float num26 = (float)(((double)powerSystem.excPool[index4].state + 1.0) * (double)entityAnimPool[entityId].working_length * 0.5);
                        if ((double)num26 >= 3.9900000095367432)
                            num26 = 3.99f;
                        entityAnimPool[entityId].time = num26;
                        entityAnimPool[entityId].state = 0U;
                        entityAnimPool[entityId].power = (float)powerSystem.excPool[index4].currPoolEnergy / (float)powerSystem.excPool[index4].maxPoolEnergy;
                        if (flag4)
                        {
                            long num27 = powerSystem.excPool[index4].OutputCaps();
                            num_exc_output_cap += num27;
                            num_gen_exc_output_cap = num_exc_output_cap;
                            powerSystem.currentGeneratorCapacities[powerSystem.excPool[index4].subId] += num27;
                        }
                        else if (flag3)
                            num_exc_input_cap += powerSystem.excPool[index4].InputCaps();
                    }
                    /* Code Analysis Note: until this line
                     * capGenAndExc = capExcDischarge = ExcOutCap
                     * num_energy_req = num_cons_req = ConsReq
                     * num_exc_input_cap = ExcInpCap
                     */
                    /* Patch note: calculate clean energy capacity */
                    long cleanEnergyGeneratorCap = 0;

                    List<int> generators = powerNetwork.generators;
                    int count3 = generators.Count;
                    for (int index5 = 0; index5 < count3; ++index5)
                    {
                        int index6 = generators[index5];
                        long num28;
                        if (powerSystem.genPool[index6].wind)
                        {
                            num28 = powerSystem.genPool[index6].EnergyCap_Wind(windStrength);
                            num_gen_exc_output_cap += num28;
                            cleanEnergyGeneratorCap += num28;   //Line Patch
                        }
                        else if (powerSystem.genPool[index6].photovoltaic)
                        {
                            if (flag2)
                            {
                                num28 = powerSystem.genPool[index6].EnergyCap_PV(normalized.x, normalized.y, normalized.z, luminosity);
                                num_gen_exc_output_cap += num28;
                            }
                            else
                            {
                                num28 = powerSystem.genPool[index6].capacityCurrentTick;
                                num_gen_exc_output_cap += num28;
                            }
                            cleanEnergyGeneratorCap += num28;  // Line Patch
                        }
                        else if (powerSystem.genPool[index6].gamma)
                        {
                            num28 = powerSystem.genPool[index6].EnergyCap_Gamma(response);
                            num_gen_exc_output_cap += num28;
                            cleanEnergyGeneratorCap += num28;  // Line Patch
                        }
                        else if (powerSystem.genPool[index6].geothermal)
                        {
                            num28 = powerSystem.genPool[index6].EnergyCap_GTH();
                            num_gen_exc_output_cap += num28;
                            cleanEnergyGeneratorCap += num28;  // Line Patch
                        }
                        else
                        {
                            num28 = powerSystem.genPool[index6].EnergyCap_Fuel();
                            num_gen_exc_output_cap += num28;
                            entitySignPool[powerSystem.genPool[index6].entityId].signType = num28 > 30L ? 0U : 8U;
                        }
                        powerSystem.currentGeneratorCapacities[powerSystem.genPool[index6].subId] += num28;
                    }
                    num_power_gen_cap += num_gen_exc_output_cap - num_exc_output_cap;
                    long num_energy_margin = num_gen_exc_output_cap - num_energy_req;
                    long num_energy_at_field_charge = 0;
                    /* Code Analysis Note: until this line
                     * capExcDischarge = ExcOutCap
                     * capGenAndExc = ExcOutCap + GenCap
                     * num_power_gen_cap = GenCap
                     * num_energy_req = num_cons_req = ConsReq
                     * num_exc_input_cap = ExcInpCap
                     * num_energy_margin = ExcOutCap + GenCap - ConsReq
                     */

                    if (num_energy_margin > 0L && powerNetwork.exportDemandRatio > 0.0)
                    {
                        if (powerNetwork.exportDemandRatio > 1.0)
                            powerNetwork.exportDemandRatio = 1.0;
                        num_energy_at_field_charge = (long)((double)num_energy_margin * powerNetwork.exportDemandRatio + 0.5);
                        num_energy_margin -= num_energy_at_field_charge;
                        num_energy_req += num_energy_at_field_charge;
                    }

                    /* Code Analysis Note: until this line
                     * NOTE: powerNetwork.exportDemandRatio = 1.0 - (1.0 - powerNetwork.exportDemandRatio) * (1.0 - planetAtField.fillDemandRatio); regarding ATFields
                     * 
                     * capExcDischarge = ExcOutCap
                     * capGenAndExc = ExcOutCap + GenCap
                     * num_power_gen_cap = GenCap
                     * num_cons_req = ConsReq
                     * num_exc_input_cap = ExcInpCap
                     * num_energy_at_field_charge = EnergyExport = (ExcOutCap + GenCap - ConsReq) * exportDemandRatio + 0.5
                     * num_energy_margin = ExcOutCap + GenCap - ConsReq - EnergyExport => this value determine Accs should charge
                     * num_energy_req = ConsReq + EnergyExport
                     */

                    powerNetwork.exportDemandRatio = 0.0;
                    powerNetwork.energyStored = 0L;
                    List<int> accumulators = powerNetwork.accumulators;
                    int count4 = accumulators.Count;
                    long num_acc_charged = 0;
                    long num_acc_discharged = 0;
                    //
                    // Do acc charge or discharge
                    //
                    if (num_energy_margin >= 0L)
                    {
                        for (int index7 = 0; index7 < count4; ++index7)
                        {
                            int index8 = accumulators[index7];
                            powerSystem.accPool[index8].curPower = 0L;
                            long num33 = powerSystem.accPool[index8].InputCap();
                            if (num33 > 0L)
                            {
                                long num34 = num33 < num_energy_margin ? num33 : num_energy_margin;
                                powerSystem.accPool[index8].curEnergy += num34;
                                powerSystem.accPool[index8].curPower = num34;
                                num_energy_margin -= num34;
                                num_acc_charged += num34;
                                num_power_charge += num34;
                            }
                            powerNetwork.energyStored += powerSystem.accPool[index8].curEnergy;
                            int entityId = powerSystem.accPool[index8].entityId;
                            entityAnimPool[entityId].state = powerSystem.accPool[index8].curEnergy > 0L ? 1U : 0U;
                            entityAnimPool[entityId].power = (float)powerSystem.accPool[index8].curEnergy / (float)powerSystem.accPool[index8].maxEnergy;
                        }
                    }
                    else
                    {
                        long num35 = -num_energy_margin;
                        for (int index9 = 0; index9 < count4; ++index9)
                        {
                            int index10 = accumulators[index9];
                            powerSystem.accPool[index10].curPower = 0L;
                            long num36 = powerSystem.accPool[index10].OutputCap();
                            if (num36 > 0L)
                            {
                                long num37 = num36 < num35 ? num36 : num35;
                                powerSystem.accPool[index10].curEnergy -= num37;
                                powerSystem.accPool[index10].curPower = -num37;
                                num35 -= num37;
                                num_acc_discharged += num37;
                                num_power_discharge += num37;
                            }
                            powerNetwork.energyStored += powerSystem.accPool[index10].curEnergy;
                            int entityId = powerSystem.accPool[index10].entityId;
                            entityAnimPool[entityId].state = powerSystem.accPool[index10].curEnergy > 0L ? 2U : 0U;
                            entityAnimPool[entityId].power = (float)powerSystem.accPool[index10].curEnergy / (float)powerSystem.accPool[index10].maxEnergy;
                        }
                    }
                    /* Code Analysis Note: until this line
                     * NOTE: powerNetwork.exportDemandRatio = 1.0 - (1.0 - powerNetwork.exportDemandRatio) * (1.0 - planetAtField.fillDemandRatio); regarding ATFields
                     * 
                     * capExcDischarge = ExcOutCap
                     * capGenAndExc = ExcOutCap + GenCap
                     * num_power_gen_cap = GenCap
                     * num_cons_req = ConsReq
                     * num_exc_input_cap = ExcInpCap
                     * num_energy_at_field_charge = EnergyExport = (ExcOutCap + GenCap - ConsReq) * exportDemandRatio + 0.5
                     * num_energy_req = ConsReq + EnergyExport
                     * num_power_charge = num_acc_charged = AccCharged
                     * num_acc_discharged = num_power_discharge = AccDischarged
                     * num_energy_margin = ExcOutCap + GenCap - ConsReq - EnergyExport - AccCharged  => this value determine following Exc should charge                     * 
                     */

                    /* Patch note : */
                    //double num_exc_charge_work_ratio = num_energy_margin < num_exc_input_cap ? (double) num_energy_margin / (double) num_exc_input_cap : 1.0;
                    num_energy_margin -= num_exc_output_cap; // Exclude exchanger output
                    num_energy_margin = num_energy_margin > 0 ? num_energy_margin : 0;
                    double num_exc_charge_work_ratio = num_energy_margin < num_exc_input_cap ? (double)num_energy_margin / (double)num_exc_input_cap : 1.0;
                    /* Patch End
                    /* Code Analysis Note: ExcChargeWorkRatio = num_exc_charge_work_ratio = num_energy_margin/num_exc_input_cap limited by <=1 */

                    //
                    // Do exchanger charge
                    //
                    for (int index11 = 0; index11 < count2; ++index11)
                    {
                        int index12 = exchangers[index11];
                        if ((double)powerSystem.excPool[index12].state >= 1.0 && num_exc_charge_work_ratio >= 0.0)
                        {
                            long num39 = (long)(num_exc_charge_work_ratio * (double)powerSystem.excPool[index12].capsCurrentTick + 0.99999);
                            long remaining = num_energy_margin < num39 ? num_energy_margin : num39;
                            long num40 = powerSystem.excPool[index12].InputUpdate(remaining, entityAnimPool, productRegister, consumeRegister);
                            num_energy_margin -= num40;
                            num_exc_charged_total += num40;
                            num_power_charge += num40;
                        }
                        else
                            powerSystem.excPool[index12].currEnergyPerTick = 0L;
                    }

                    /* Code Analysis Note: until this line
                     * NOTE: powerNetwork.exportDemandRatio = 1.0 - (1.0 - powerNetwork.exportDemandRatio) * (1.0 - planetAtField.fillDemandRatio); regarding ATFields
                     * 
                     * capExcDischarge = ExcOutCap
                     * capGenAndExc = ExcOutCap + GenCap
                     * num_power_gen_cap = GenCap
                     * num_cons_req = ConsReq
                     * num_exc_input_cap = ExcInpCap
                     * num_energy_at_field_charge = EnergyExport = (ExcOutCap + GenCap - ConsReq) * exportDemandRatio + 0.5
                     * num_energy_req = ConsReq + EnergyExport
                     * num_energy_margin = ExcOutCap + GenCap - ConsReq - EnergyExport - AccCharged - ExcCharged  => should be 0 that remain nothing
                     * num_exc_charged_total = ExcCharged => Exchanger has lowest charge priority
                     * num_power_charge = AccCharged + ExcCharged
                     * num_acc_charged = AccCharged
                     * num_acc_discharged = num_power_discharge = AccDischarged
                     */

                    // energyDebt = (ExcOutCap + GenCap) < (ConsReq + EnergyExport) + ExcCharged ? (ExcOutCap + GenCap) + AccCharged + ExcCharged :  (CosmPowerReq + ExportReq) +  AccCharged + ExcCharged; 
                    // Following loop will calculate energy actually discharged, energyDebt will determine energy should be discharged from exchangers
                    long num_energy_debt = num_gen_exc_output_cap < num_energy_req + num_exc_charged_total ? num_gen_exc_output_cap + num_acc_charged + num_exc_charged_total : num_energy_req + num_acc_charged + num_exc_charged_total;
                    //Patch note: change the exchanger discharge target
                    double num_exc_discharge_ratio;
                    num_exc_discharge_ratio = CalculateDischargeRatio(num_gen_exc_output_cap, num_exc_output_cap, cleanEnergyGeneratorCap, num_energy_debt);

                    //
                    // Do exchanger discharge
                    //
                    for (int index13 = 0; index13 < count2; ++index13)
                    {
                        int index14 = exchangers[index13];
                        if ((double)powerSystem.excPool[index14].state <= -1.0)
                        {
                            long num43 = (long)(num_exc_discharge_ratio * (double)powerSystem.excPool[index14].capsCurrentTick + 0.99999);
                            long energyPay = num_energy_debt < num43 ? num_energy_debt : num43;
                            long num44 = powerSystem.excPool[index14].OutputUpdate(energyPay, entityAnimPool, productRegister, consumeRegister);
                            num_exc_discharged_total += num44;
                            num_power_discharge += num44;
                            num_energy_debt -= num44;
                        }
                    }

                    /* Code Analysis Note: until this line
                     * NOTE: powerNetwork.exportDemandRatio = 1.0 - (1.0 - powerNetwork.exportDemandRatio) * (1.0 - planetAtField.fillDemandRatio); regarding ATFields
                     * 
                     * capExcDischarge = ExcOutCap
                     * capGenAndExc = ExcOutCap + GenCap
                     * num_power_gen_cap = GenCap
                     * num_cons_req = ConsReq
                     * num_exc_input_cap = ExcInpCap
                     * num_energy_at_field_charge = EnergyExport = (ExcOutCap + GenCap - ConsReq) * exportDemandRatio + 0.5
                     * num_energy_req = ConsReq + EnergyExport
                     * num_energy_margin = ExcOutCap + GenCap - ConsReq - EnergyExport - AccCharged - ExcCharged  => should be 0 that remain nothing
                     * num_exc_charged_total = ExcCharged => Exchanger has lowest charge priority
                     * num_power_charge = AccCharged + ExcCharged = TotalCharge
                     * num_acc_charged = AccCharged
                     * num_acc_discharged = AccDischarged
                     * num_power_discharge = AccDischarged + ExcDischarged = TotalDischarge
                     * energyDebt = (ExcOutCap + GenCap) + AccCharged + ExcCharged - ExcDischarged =>(not enough power)
                     * or    = (CosmPowerReq + EnergyExport) +  AccCharged + ExcCharged  - ExcDischarged=>(enough power)
                     * excDischargeRatio = ExcDischargeWorkRatio
                     * num_exc_discharged_total = num_power_discharge = ExcDischarged
                     */

                    powerNetwork.energyCapacity = num_gen_exc_output_cap - num_exc_output_cap;//GenCap = (ExcOutCap + GenCap) - ExcOutCap
                    powerNetwork.energyRequired = num_energy_req - num_energy_at_field_charge;//ConsReq = (CosmReq + EnergyExport) - EnergyExport
                    powerNetwork.energyExport = num_energy_at_field_charge;//EnergyExport
                    powerNetwork.energyServed = num_gen_exc_output_cap + num_acc_discharged < num_energy_req ? num_gen_exc_output_cap + num_acc_discharged : num_energy_req;// ExcOutCap + GenCap + AccDischarged < (CosmPowerReq + EnergyExport)？ ExcOutCap + GenCap + AccDischarged  ： (CosmPowerReq + ExportReq)
                    powerNetwork.energyAccumulated = num_acc_charged - num_acc_discharged;//AccEnergy = AccCharged - AccDischarged
                    powerNetwork.energyExchanged = num_exc_charged_total - num_exc_discharged_total;//ExcCharged - ExcDischarged
                    powerNetwork.energyExchangedInputTotal = num_exc_charged_total;//ExcCharged
                    powerNetwork.energyExchangedOutputTotal = num_exc_discharged_total;//ExcDischarged
                    if (num_energy_at_field_charge > 0L)
                    {
                        PlanetATField planetAtField = powerSystem.factory.planetATField;
                        planetAtField.energy += num_energy_at_field_charge;
                        planetAtField.atFieldRechargeCurrent = num_energy_at_field_charge * 60L;
                    }
                    long num_power_cap_with_acc = num_gen_exc_output_cap + num_acc_discharged;
                    long num_power_req_with_acc = num_energy_req + num_acc_charged;
                    // if (TotalPowerCap > TotalPowerReq) (num_energy_cons_stat = CosmPowerReq + energyExport) else num_energy_cons_stat = totalPowerCap
                    num_energy_cons_stat += num_power_cap_with_acc >= num_power_req_with_acc ? num_cons_req + num_energy_at_field_charge : num_power_cap_with_acc;
                    // ExcOutputMargin = (ExcDischarged - TotalPowerReq) under limited by 0??
                    long num_energy_margin_with_only_exc = num_exc_discharged_total - num_power_req_with_acc > 0L ? num_exc_discharged_total - num_power_req_with_acc : 0L;
                    //ConsumerRatio = TotalPowerCap / TotalPowerReq, upper limited by 1
                    double num_cons_ratio = num_power_cap_with_acc >= num_power_req_with_acc ? 1.0 : (double)num_power_cap_with_acc / (double)num_power_req_with_acc;
                    // ? num_energy_need_serve = TotalPowerReq + ExcCharged - max(0, (ExcDischarged - TotalPowerReq)) almost = TotalPowerReq + ExcCharged
                    long num_energy_need_serve = num_power_req_with_acc + (num_exc_charged_total - num_energy_margin_with_only_exc);
                    // ?????EnergyCanServeByAccAndGen = (ExcOutCap + GenCap) + AccDischarged - ExcDischarged
                    long num_energy_can_serve = num_power_cap_with_acc - num_exc_discharged_total;
                    //GenerateRatio =  EnergyReqGen/EnergyCanServeByAccAndGen upper limited by 1
                    double num_gen_work_ratio = num_energy_can_serve > num_energy_need_serve ? (double)num_energy_need_serve / (double)num_energy_can_serve : 1.0;
                    powerNetwork.consumerRatio = num_cons_ratio;
                    powerNetwork.generaterRatio = num_gen_work_ratio;
                    float num_network_serve_ratio = num_energy_can_serve > 0L || powerNetwork.energyStored > 0L || num_exc_discharged_total > 0L ? (float)num_cons_ratio : 0.0f;
                    //If not EnergyReqGen or energyStored or ExcOutput, set 0
                    float num_network_gen_ratio = num_energy_can_serve > 0L || powerNetwork.energyStored > 0L || num_exc_discharged_total > 0L ? (float)num_gen_work_ratio : 0.0f;
                    powerSystem.networkServes[index1] = num_network_serve_ratio;
                    powerSystem.networkGenerates[index1] = num_network_gen_ratio;

                    double generatorRatioClean, generatorRatioFuel;
                    CalculateGeneratorLoadRatio(powerNetwork, cleanEnergyGeneratorCap, num_energy_debt, out generatorRatioClean, out generatorRatioFuel);

                    // Do energy request distribution on each generator
                    for (int index15 = 0; index15 < count3; ++index15)
                    {
                        int index16 = generators[index15];
                        long energy = 0;
                        float _speed1 = 1f;
                        bool flag5 = !powerSystem.genPool[index16].wind && !powerSystem.genPool[index16].photovoltaic && !powerSystem.genPool[index16].gamma && !powerSystem.genPool[index16].geothermal;
                        if (flag5)
                            powerSystem.genPool[index16].currentStrength = num_energy_debt <= 0L || powerSystem.genPool[index16].capacityCurrentTick <= 0L ? 0.0f : 1f;
                        if (num_energy_debt > 0L && powerSystem.genPool[index16].productId == 0)
                        {
                            /* Patch note: if generator uses clean energy, make it output max energy */
                            double generaterRatio = flag5 ? generatorRatioFuel : generatorRatioClean;
                            long num54 = (long)(generaterRatio * (double)powerSystem.genPool[index16].capacityCurrentTick + 0.99999);

                            energy = num_energy_debt < num54 ? num_energy_debt : num54;
                            if (energy > 0L)
                            {
                                num_energy_debt -= energy;
                                if (flag5)
                                {
                                    powerSystem.genPool[index16].GenEnergyByFuel(energy, consumeRegister);
                                    _speed1 = 2f;
                                }
                            }
                        }
                        powerSystem.genPool[index16].generateCurrentTick = energy;
                        int entityId = powerSystem.genPool[index16].entityId;
                        if (powerSystem.genPool[index16].wind)
                        {
                            float _speed2 = 0.7f;
                            entityAnimPool[entityId].Step2((double)entityAnimPool[entityId].power > 0.10000000149011612 || energy > 0L ? 1U : 0U, _dt, windStrength, _speed2);
                        }
                        else if (powerSystem.genPool[index16].gamma)
                        {
                            bool keyFrame = (index16 + num9) % 90 == 0;
                            powerSystem.genPool[index16].GameTick_Gamma(useIonLayer, useCata, keyFrame, powerSystem.factory, productRegister, consumeRegister);
                            entityAnimPool[entityId].time += _dt;
                            if ((double)entityAnimPool[entityId].time > 1.0)
                                --entityAnimPool[entityId].time;
                            entityAnimPool[entityId].power = (float)powerSystem.genPool[index16].capacityCurrentTick / (float)powerSystem.genPool[index16].genEnergyPerTick;
                            entityAnimPool[entityId].state = (uint)((powerSystem.genPool[index16].productId > 0 ? 2 : 0) + (powerSystem.genPool[index16].catalystPoint > 0 ? 1 : 0));
                            entityAnimPool[entityId].working_length = (float)((double)entityAnimPool[entityId].working_length * 0.99000000953674316 + (powerSystem.genPool[index16].catalystPoint > 0 ? 0.0099999997764825821 : 0.0));
                            if (isActive)
                                entitySignPool[entityId].signType = (double)powerSystem.genPool[index16].productCount < 20.0 ? 0U : 6U;
                        }
                        else if (powerSystem.genPool[index16].fuelMask > (short)1)
                        {
                            float _power = (float)((double)entityAnimPool[entityId].power * 0.98 + 0.02 * (energy > 0L ? 1.0 : 0.0));
                            if (energy > 0L && (double)_power < 0.0)
                                _power = 0.0f;
                            entityAnimPool[entityId].Step2((double)entityAnimPool[entityId].power > 0.10000000149011612 || energy > 0L ? 1U : 0U, _dt, _power, _speed1);
                        }
                        else if (powerSystem.genPool[index16].geothermal)
                        {
                            float num55 = powerSystem.genPool[index16].warmup + powerSystem.genPool[index16].warmupSpeed;
                            powerSystem.genPool[index16].warmup = (double)num55 > 1.0 ? 1f : ((double)num55 < 0.0 ? 0.0f : num55);
                            entityAnimPool[entityId].state = energy > 0L ? 1U : 0U;
                            entityAnimPool[entityId].Step(entityAnimPool[entityId].state, _dt, 2f, 0.0f);
                            entityAnimPool[entityId].working_length = powerSystem.genPool[index16].warmup;
                            if (energy > 0L)
                            {
                                if ((double)entityAnimPool[entityId].power < 1.0)
                                    entityAnimPool[entityId].power += _dt / 6f;
                            }
                            else if ((double)entityAnimPool[entityId].power > 0.0)
                                entityAnimPool[entityId].power -= _dt / 6f;
                            entityAnimPool[entityId].prepare_length += (float)(3.1415927410125732 * (double)_dt / 8.0);
                            if ((double)entityAnimPool[entityId].prepare_length > 6.2831854820251465)
                                entityAnimPool[entityId].prepare_length -= 6.28318548f;
                        }
                        else
                        {
                            float _power = (float)((double)entityAnimPool[entityId].power * 0.98 + 0.02 * (double)energy / (double)powerSystem.genPool[index16].genEnergyPerTick);
                            if (energy > 0L && (double)_power < 0.20000000298023224)
                                _power = 0.2f;
                            entityAnimPool[entityId].Step2((double)entityAnimPool[entityId].power > 0.10000000149011612 || energy > 0L ? 1U : 0U, _dt, _power, _speed1);
                        }
                    }
                }
            }
            lock (factoryProductionStat)
            {
                factoryProductionStat.powerGenRegister = num_power_gen_cap;
                factoryProductionStat.powerConRegister = num_cons_req;
                factoryProductionStat.powerDisRegister = num_power_discharge;
                factoryProductionStat.powerChaRegister = num_power_charge;
                factoryProductionStat.energyConsumption += num_energy_cons_stat;
            }
            if (isActive)
            {
                for (int index17 = 0; index17 < powerSystem.netCursor; ++index17)
                {
                    PowerNetwork powerNetwork = powerSystem.netPool[index17];
                    if (powerNetwork != null && powerNetwork.id == index17)
                    {
                        List<int> consumers = powerNetwork.consumers;
                        int count = consumers.Count;
                        if (index17 == 0)
                        {
                            for (int index18 = 0; index18 < count; ++index18)
                                entitySignPool[powerSystem.consumerPool[consumers[index18]].entityId].signType = 1U;
                        }
                        else if (powerNetwork.consumerRatio < 0.10000000149011612)
                        {
                            for (int index19 = 0; index19 < count; ++index19)
                                entitySignPool[powerSystem.consumerPool[consumers[index19]].entityId].signType = 2U;
                        }
                        else if (powerNetwork.consumerRatio < 0.5)
                        {
                            for (int index20 = 0; index20 < count; ++index20)
                                entitySignPool[powerSystem.consumerPool[consumers[index20]].entityId].signType = 3U;
                        }
                        else
                        {
                            for (int index21 = 0; index21 < count; ++index21)
                                entitySignPool[powerSystem.consumerPool[consumers[index21]].entityId].signType = 0U;
                        }
                    }
                }
            }
            for (int index = 1; index < powerSystem.nodeCursor; ++index)
            {
                if (powerSystem.nodePool[index].id == index)
                {
                    int entityId = powerSystem.nodePool[index].entityId;
                    int networkId = powerSystem.nodePool[index].networkId;
                    if (powerSystem.nodePool[index].isCharger)
                    {
                        float networkServe = powerSystem.networkServes[networkId];
                        int num56 = powerSystem.nodePool[index].requiredEnergy - powerSystem.nodePool[index].idleEnergyPerTick;
                        if ((double)powerSystem.nodePool[index].coverRadius < 20.0)
                            entityAnimPool[entityId].StepPoweredClamped(networkServe, _dt, num56 > 0 ? 2U : 1U);
                        else
                            entityAnimPool[entityId].StepPoweredClamped2(networkServe, _dt, num56 > 0 ? 2U : 1U);
                        if (num56 > 0 && entityAnimPool[entityId].state == 2U)
                        {
                            lock (mainPlayer.mecha)
                            {
                                int change = (int)((double)num56 * (double)networkServe);
                                mainPlayer.mecha.coreEnergy += (double)change;
                                mainPlayer.mecha.MarkEnergyChange(2, (double)change);
                                mainPlayer.mecha.AddChargerDevice(entityId);
                                if (mainPlayer.mecha.coreEnergy > mainPlayer.mecha.coreEnergyCap)
                                    mainPlayer.mecha.coreEnergy = mainPlayer.mecha.coreEnergyCap;
                            }
                        }
                    }
                    else if (entityPool[entityId].powerGenId == 0 && entityPool[entityId].powerAccId == 0 && entityPool[entityId].powerExcId == 0)
                    {
                        float networkServe = powerSystem.networkServes[networkId];
                        entityAnimPool[entityId].Step2((double)networkServe > 0.10000000149011612 ? 1U : 0U, _dt, (float)((double)entityAnimPool[entityId].power * 0.97 + 0.03 * (double)networkServe), 0.4f);
                    }
                }
            }
            ExecuteTime[powerSystem.factory.index] = ExecuteTime[powerSystem.factory.index] * 0.99 + StopWatchList[powerSystem.factory.index].duration * 0.01;
            /** Patch note, skip the origin function **/
            return false;
        }

        private static void CalculateGeneratorLoadRatio(PowerNetwork powerNetwork, long cleanEnergyGeneratorCap, long num_energy_debt, out double generatorRatioClean, out double generatorRatioFuel)
        {
            //Patch Note : Calculate clean energy generator generaterRatio, and fuel generaterRatio seperately and apply to them 
            long totalEnergyNeedGenerate = num_energy_debt;
            long fuelEnergyCap = powerNetwork.energyCapacity - cleanEnergyGeneratorCap;
            generatorRatioClean = 1.0;

            // Different from origin calculation which ignores energy input energy from exchanger => some time generator may stop
            // this ratio will make every fuel works equally
            generatorRatioFuel = ((double)totalEnergyNeedGenerate - (double)cleanEnergyGeneratorCap) / ((double)fuelEnergyCap + 0.001);
            generatorRatioFuel = generatorRatioFuel > 1.0 ? 1.0 : generatorRatioFuel;

            if (generatorRatioFuel < 0.0)
            {
                generatorRatioFuel = 0.0;
                generatorRatioClean = (double)totalEnergyNeedGenerate / ((double)cleanEnergyGeneratorCap + 0.001);
                generatorRatioClean = generatorRatioClean > 1.0 ? 1.0 : generatorRatioClean;
            }
        }

        private static double CalculateDischargeRatio(long capGenAndExc, long capExcDischarge, long capGreenGenerator, long energyDebt)
        {
            double excDischargeRatio;
            long energyToProduce;
            long energyToExcDischarge;

            switch (ExchangerDischargeStrategy)
            {
                default:
                case EExchangerDischargeStrategy.EQUAL_AS_FUEL_GENERATOR:
                    // Fuel generators and exchangers discharge capacity
                    long fuelAndDiscCap = (capGenAndExc - capGreenGenerator);

                    // Assumes green generators works with full load, other facilities should generates remain
                    energyToProduce = energyDebt - capGreenGenerator;

                    // Equation:
                    // (FuelCap +  ExcCap) * ExcDischargeRatio = EnergyToProduce = (Debt - GreenGenCap)
                    excDischargeRatio = (fuelAndDiscCap <= 0 || energyToProduce <= 0) ? 0.0 :
                            (energyToProduce < fuelAndDiscCap) ? ((double)energyToProduce / (double)fuelAndDiscCap) : 1.0;
                    break;

                case EExchangerDischargeStrategy.DISCHARGE_FIRST:
                    // Discharge all power outside the capability of green generators 
                    // If only greenGenerators is enough, no need to discharge
                    energyToExcDischarge = energyDebt - capGreenGenerator;
                    energyToExcDischarge = energyToExcDischarge < 0 ? 0 : energyToExcDischarge;

                    // Equation:
                    // ExcDischargeCap * ExcDischargeRatio = energyToExcDischarge = (Debt - GreenGenCap)
                    excDischargeRatio = energyToExcDischarge < capExcDischarge ? (double)energyToExcDischarge / (double)capExcDischarge : 1.0;
                    break;

                case EExchangerDischargeStrategy.DISCHARGE_LAST:
                    // Only discharge power outside the capability of all generators capability
                    energyToExcDischarge = energyDebt - (capGenAndExc - capExcDischarge);
                    energyToExcDischarge = energyToExcDischarge < 0 ? 0 : energyToExcDischarge;

                    // Equation:
                    // ExcDischargeCap * ExcDischargeRatio = energyToExcDischarge = (Debt - (FuelCap + GreenGenCap))
                    excDischargeRatio = energyToExcDischarge < capExcDischarge ? (double)energyToExcDischarge / (double)capExcDischarge : 1.0;
                    break;
            }

            return excDischargeRatio;
        }
    }
}
