﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RiverSystem.Attributes;
using RiverSystem.Catchments.Models.ContaminantGenerationModels;
using RiverSystem.Catchments.Models.RiparianParticulateModel;
using TIME.Core;
using TIME.Core.Metadata;
using TIME.ManagedExtensions;
using TIME.Models.Components;

namespace Source.CLOE
{
    public abstract class AbstractCLOEFUModel : StandardConstituentGenerationModel
    {
        #region Initial state parameters

        [Parameter, CalculationUnits(CommonUnits.kilograms),Minimum(0.0)]
        public double InitialSoilStore { get; set; }

        [Parameter, CalculationUnits(CommonUnits.kilograms), Minimum(0.0)]
        public double InitialGroundwaterStore { get; set; }

        #endregion

        #region States

        [State, CalculationUnits(CommonUnits.kilograms)]
        public double SoilStore { get; set; }

        [State, CalculationUnits(CommonUnits.kilograms)]
        public double GroundwaterStore { get; set; }

        #endregion

        #region Generation terms

        [Parameter, Description("O: Source "), Minimum(0.0), Maximum(1.0), DefaultValue(1.0)]
        public double O { get; set; }

        [Parameter, Description("Alpha: Mass conversion coefficient")]
        public double Alpha { get; set; }

        [Input, Description("InputRate (R)"), CalculationUnits(CommonUnits.kilograms), ConstituentLinkage]
        public double InputRate { get; set; }

        [Input, Description("Efficiency term(M)")]
        public double E { get; set; }

        [Input, DefaultValue(1.0)]
        public double TimingFactor { get; set; }

        #endregion

        // TODO InputRate should be a list, along with List<double> P factor

        #region Management modifiers

        [Input, Description("Management modifier - Generation (M)")]
        public double M { get; set; }

        [Input, Description("Management modifier - Groundcover (M3)")]
        public double M3 { get; set; }

        [Input, Description("Management modifier - Riparian (M4)")]
        public double M4 { get; set; }

        [Input, Description("Management modifier - Wetlands (M5)")]
        public double M5 { get; set; }

        [Input, Description("Management modifier - Soil (M6)")]
        public double M6 { get; set; }

        [Input, Description("Management modifier - Plant uptake (M13)")]
        public double M13 { get; set; }

        [Input, Description("Management modifier - Soil PRI (M22)")]
        public double M22 { get; set; }

        [Input, Description("Management modifier - DOC (M41)")]
        public double M41 { get; set; }
        #endregion

        #region Betas

        [Parameter, Description("B1: Quickflow coefficient")]
        public double B1 { get; set; }

        [Parameter, Description("B2: Distance coefficient")]
        public double B2 { get; set; }

        [Parameter, Description("B3: Groundcover coefficient")]
        public double B3 { get; set; }

        [Parameter, Description("B4: Riparian coefficient")]
        public double B4 { get; set; }

        [Parameter, Description("B5: Wetlands coefficient")]
        public double B5 { get; set; }

        [Parameter, Description("B6: Soil coefficient")]
        public double B6 { get; set; }

        [Parameter, Description("B11: Temperature coefficient")]
        public double B11 { get; set; }

        [Parameter, Description("B12: Soilmoisture coefficient")]
        public double B12 { get; set; }

        [Parameter, Description("B13: Plant uptake coefficient")]
        public double B13 { get; set; }

        [Parameter, Description("B21: Drainage coefficient")]
        public double B21 { get; set; }

        [Parameter, Description("B22: Soil Leach coefficient")]
        public double B22 { get; set; }

        [Parameter, Description("B31: Groundwater discharge coefficient")]
        public double B31 { get; set; }

        [Parameter, Description("B41: DOC coefficient")]
        public double B41 { get; set; }

        [Parameter, Description("B42: Geology coefficient")]
        public double B42 { get; set; }

        #endregion

        #region Loss delivery controls
        [Input, Description("T: Store loss threshold"),Minimum(0.0),Maximum(1.0),DefaultValue(1.0)]
        public double T { get; set; }

        [Parameter, Description("Delivery Ratio - Groundwater to Stream")]
        public double DgwSurf { get; set; }

        [Parameter, Description("Delivery Function Scaling - Groundwater to Stream")]
        public double OgwSurf { get; set; }

        [Parameter, Description("Delivery Ratio - Loss out of system from Groundwater")]
        public double DgwOut { get; set; }

        [Parameter, Description("Delivery Function Scaling - Loss out of system from Groundwater")]
        public double OgwOut { get; set; }

        [Parameter, Description("Delivery Ratio - Soil to Groundwater")]
        public double Dgw { get; set; }

        [Parameter, Description("Delivery Function Scaling - Soil to Groundwater")]
        public double Ogw { get; set; }

        [Parameter, Description("Delivery Ratio - Soil to Stream")]
        public double Dsurf { get; set; }

        [Parameter, Description("Delivery Function Scaling - Soil to Stream")]
        public double Osurf { get; set; }

        [Parameter, Description("Delivery Ratio - Loss out of system")]
        public double Dout { get; set; }

        [Parameter, Description("Delivery Function Scaling - Loss out of system")]
        public double Oout { get; set; }

        #endregion

        #region Loss flux terms

        [Input]
        public double Geology { get; set; }

        [Input]
        public double DOC { get; set; }

        [Input]
        public double Drainage { get; set; }

        [Input]
        public double SoilLeach { get; set; }

        [Input]
        public double Groundcover { get; set; }

        [Input]
        public double Distance { get; set; }

        [Input]
        public double Wetlands { get; set; }

        [Input]
        public double PlantUptake { get; set; }

        [Input]
        public double SoilMoisture { get; set; }

        [Input]
        public double Temp { get; set; }

        [Input, Description("Riparian nutrient reduction rate")]
        public double Riparian { get; set; }

        [Input, Description("PERI (Cowel P/PBI)")]
        public double Soil { get; set; }

        [Input]
        public double GWDischarge { get; set; }
        #endregion

        #region Outputs

        [Output, CalculationUnits(CommonUnits.KgPerSec)]
        public double LossOut { get; set; }

        [Output, CalculationUnits(CommonUnits.KgPerSec)]
        public double LossToGroundwater { get; set; }

        [Output, CalculationUnits(CommonUnits.KgPerSec)]
        public double LossOutGroundwater { get; set; }

        #endregion

        public override void runTimeStep(DateTime now, double theTimeStepInSeconds)
        {
            // TODO check ordering and constraining with Baihua
            GWDischarge = slowflow;

            SoilStore += GenerateRateKg_timestep;//*theTimeStepInSeconds;

            CalculateSurfaceFluxes(theTimeStepInSeconds);

            CalculateGroundwaterStoreFluxes(theTimeStepInSeconds);
        }

        private void CalculateGroundwaterStoreFluxes(double theTimeStepInSeconds)
        {
            // Calculate based on current store
            LossOutGroundwater = GroundwaterStore*GroundwaterLossOutRate;
            slowflowConstituent = GroundwaterStore*GroundwaterLossSlowflowRate;

            // Constrain based on residual store
            LossOutGroundwater = Math.Min(GroundwaterStore, LossOutGroundwater);
            GroundwaterStore -= LossOutGroundwater;

            slowflowConstituent = Math.Min(GroundwaterStore,slowflowConstituent);
            GroundwaterStore -= slowflowConstituent;

            // Convert fluxes to rates
            LossOutGroundwater /= theTimeStepInSeconds;
            slowflowConstituent /= theTimeStepInSeconds;
        }

        private void CalculateSurfaceFluxes(double theTimeStepInSeconds)
        {
            var lossThreshold = T * SoilStore;

            // Calculate all based on current SoilStore
            LossOut = SoilStore*OutsideLossRate;
            quickflowConstituent = SoilStore*SurfaceLossRate; // SurfaceLoss
            LossToGroundwater = SoilStore*GroundwaterLossRate;

            var lossDemand = LossOut + quickflowConstituent + LossToGroundwater;
            var lossScale = 1.0;
            if (lossDemand > lossThreshold)
            {
                lossScale = lossThreshold/lossDemand;
            }

            // Constraint losses based on residual soil store
            quickflowConstituent *= lossScale;
            SoilStore -= quickflowConstituent;

            LossToGroundwater *= lossScale;
            SoilStore -= LossToGroundwater;
            GroundwaterStore += LossToGroundwater;

            LossOut *= lossScale;
            LossOut = Math.Min(LossOut, SoilStore); // Round here if necessary
            SoilStore -= LossOut;

            LossOut /= theTimeStepInSeconds; // => kg/s
            quickflowConstituent /= theTimeStepInSeconds;
            LossToGroundwater /= theTimeStepInSeconds;
        }

        public double OutsideLossRate
        {
            get
            {
                var exponent = (-B11*CloeUtils.safeInv(Temp)) + (-B12*CloeUtils.safeInv(SoilMoisture)) + (-B13*M13*CloeUtils.safeInv(PlantUptake));
                return CloeUtils.weight(Dout,Oout,Math.Exp(exponent));
            }
        }

        public double SurfaceLossRate
        {
            get
            {
                var exponent = (-B1*CloeUtils.safeInv(quickflow)) +
//                               (-B2*CloeUtils.safeInv(slowflow)) +
                               (-B2 * Distance) +
                               (-B3*M3*Groundcover) +
                               (-B4*M4*Riparian) +
                               (-B5*M5*Wetlands) +
                               (-B6*M6*Soil);
                return CloeUtils.weight(Dsurf,Osurf,Math.Exp(exponent));
            }
        }

        public double GroundwaterLossRate
        {
            get
            {
                var exponent = -B21*CloeUtils.safeInv(Drainage) - B22*M22*SoilLeach;
                return CloeUtils.weight(Dgw,Ogw,Math.Exp(exponent));
            }
        }

        public double GroundwaterLossOutRate
        {
            get
            {
                var exponent = (-B41*M41*CloeUtils.safeInv(DOC)) + (-B42*Geology);
                return CloeUtils.weight(DgwOut,OgwOut,Math.Exp(exponent));
            }
        }

        public double GroundwaterLossSlowflowRate
        {
            get
            {
                var exponent = -B31*CloeUtils.safeInv(GWDischarge);
                return CloeUtils.weight(DgwSurf,OgwSurf,Math.Exp(exponent));
            }
        }

        public virtual double GenerateRateKg_timestep
        {
            get { return O*Alpha*InputRate*M*E*TimingFactor; }
        }


        public override void reset()
        {
            base.reset();
            SoilStore = InitialSoilStore;
            GroundwaterStore = InitialGroundwaterStore;
        }
    }

}
