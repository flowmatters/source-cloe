using System;
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

        [Input, Description("Management modifier - Groundcover (Mg)")]
        public double Mg { get; set; }

        [Input, Description("Management modifier - Soil (Ms)")]
        public double Ms { get; set; }

        [Input, Description("Management modifier - Wetlands (Mw)")]
        public double Mw { get; set; }

        [Input, Description("Management modifier - Riparian (Mr)")]
        public double Mr { get; set; }

        #endregion

        #region Betas

        [Parameter, Description("B1: Quickflow coefficient")]
        public double B1 { get; set; }

        [Parameter, Description("B2: Slowflow coefficient")]
        public double B2 { get; set; }

        [Parameter, Description("B3: Distance coefficient")]
        public double B3 { get; set; }

        [Parameter, Description("B4: Groundcover coefficient")]
        public double B4 { get; set; }

        [Parameter, Description("B5: Riparian coefficient")]
        public double B5 { get; set; }

        [Parameter, Description("B6: Wetlands coefficient")]
        public double B6 { get; set; }

        [Parameter, Description("B7: Soil coefficient")]
        public double B7 { get; set; }

        [Parameter, Description("B11: Temperature coefficient")]
        public double B11 { get; set; }

        [Parameter, Description("B12: Soilmoisture coefficient")]
        public double B12 { get; set; }

        [Parameter, Description("B13: Plant uptake coefficient")]
        public double B13 { get; set; }

        [Parameter, Description("B21: Drainage coefficient")]
        public double B21 { get; set; }

        [Parameter, Description("B31: Groundwater discharge coefficient")]
        public double B31 { get; set; }

        [Parameter, Description("B41: DOC coefficient")]
        public double B41 { get; set; }

        [Parameter, Description("B42: Geology coefficient")]
        public double B42 { get; set; }

        #endregion

        #region Loss delivery controls

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

            SoilStore += GenerateRateKg_S*theTimeStepInSeconds;

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

            slowflowConstituent = Math.Min(slowflowConstituent, LossOutGroundwater);
            GroundwaterStore -= slowflowConstituent;

            // Convert fluxes to rates
            LossOutGroundwater /= theTimeStepInSeconds;
            slowflowConstituent /= theTimeStepInSeconds;
        }

        private void CalculateSurfaceFluxes(double theTimeStepInSeconds)
        {
            // Calculate all based on current SoilStore
            LossOut = SoilStore*OutsideLossRate;
            quickflowConstituent = SoilStore*SurfaceLossRate; // SurfaceLoss
            LossToGroundwater = SoilStore*GroundwaterLossRate;

            // Constraint losses based on residual soil store
            quickflowConstituent = Math.Min(SoilStore, quickflowConstituent);
            SoilStore -= quickflowConstituent;

            LossToGroundwater = Math.Min(SoilStore, LossToGroundwater);
            SoilStore -= LossToGroundwater;
            GroundwaterStore += LossToGroundwater;

            LossOut = Math.Min(SoilStore, LossOut);
            SoilStore -= LossOut;

            LossOut /= theTimeStepInSeconds; // => kg/s
            quickflowConstituent /= theTimeStepInSeconds;
            LossToGroundwater /= theTimeStepInSeconds;
        }

        public double OutsideLossRate
        {
            get
            {
                var exponent = (-B11*Temp) + (-B12*SoilMoisture) + (-B13*PlantUptake);
                return Dout + Oout*Math.Exp(exponent);
            }
        }

        public double SurfaceLossRate
        {
            get
            {
                var exponent = (-B1*quickflow) +
                               (-B2*slowflow) +
                               (-B4*Mg*Groundcover) +
                               (-B3*Distance) +
                               (-B5*Mr*Riparian) +
                               (-B6*Mw*Wetlands) +
                               (-B7*Ms*Soil);
                return Dsurf + Osurf*Math.Exp(exponent);
            }
        }

        public double GroundwaterLossRate
        {
            get
            {
                var exponent = -B21*Drainage;
                return Dgw + Ogw*Math.Exp(exponent);
            }
        }

        public double GroundwaterLossOutRate
        {
            get
            {
                var exponent = (-B41*DOC) + (-B42*Geology);
                return DgwOut + OgwOut*Math.Exp(exponent);
            }
        }

        public double GroundwaterLossSlowflowRate
        {
            get
            {
                var exponent = -B31*GWDischarge;
                return DgwSurf + OgwSurf*Math.Exp(exponent);
            }
        }

        public virtual double GenerateRateKg_S
        {
            get { return O*Alpha*InputRate*M*E*TimingFactor; }
        }


        public override void reset()
        {
            base.reset();
            SoilStore = 0.0;
            GroundwaterStore = 0.0;
        }
    }

}
