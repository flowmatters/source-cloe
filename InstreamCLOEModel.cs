using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RiverSystem;
using TIME.Core;
using TIME.Core.Metadata;

namespace Source.CLOE
{
    public class InstreamCLOEModel : LinkSourceSinkModel
    {
        public InstreamCLOEModel()
        {
            
        }

        [Parameter, CalculationUnits(CommonUnits.kilograms), Minimum(0.0)]
        public double InitialLinkStore{ get; set; }

        [State]
        public double LinkStore { get; set; }

        [Parameter]
        public double Dlink { get; set; }

        [Parameter]
        public double Olink { get; set; }

        [Parameter]
        public double B51 { get; set; }

        [Parameter]
        public double B52 { get; set; }

        #region Streambank Erosion terms
        [Input,CalculationUnits(CommonUnits.KgPerDay)]
        public double BankErosionRate { get; set; }

        [Input]
        public double TimingFactor { get; set; }

        [Parameter]
        public double Alpha { get; set; }

        [Parameter, DefaultValue(1.4), Description("Operation Factor")]
        public double O { get; set; }

        [Parameter, Description("Management Factor")]
        public double M { get; set; }

        [Parameter,Description("Streambank Vegetation Factor")]
        public double E { get; set; }

        #endregion

        public override void runTimeStep(DateTime now, double theTimeStepInSeconds)
        {
            LinkStore += AdditionalInflowMass + CatchmentInflowMass + UpstreamFlowMass;
            LinkStore += BankErosion;

            ProcessedLoad = LinkStore*ConstituentOutFraction;
            LinkStore = Math.Max(0.0, LinkStore - ProcessedLoad);
        }

        public double ConstituentOutFraction
        {
            get
            {
                var exp = -B51*CloeUtils.safeInv(DownstreamFlowVolume) + B52 * Link.TravelTime;
                return CloeUtils.weight(Dlink,Olink,Math.Exp(exp));
            }
        }

        public double BankErosion
        {
            get
            {
                return O*Alpha*BankErosionRate*M*E*TimingFactor;
                //Math.Pow(DownstreamFlowVolume, B) * Link.Length;
            }
        }

        public override LinkSourceSinkModel CloneForMultipleDivisions()
        {
            return new InstreamCLOEModel()
            {
                Dlink = Dlink,
                Olink = Olink,
                B51 = B51,
                B52 = B52,
                Alpha = Alpha,
                O = O,
                M = M,
                E= E,
                TimingFactor = TimingFactor,
                BankErosionRate = BankErosionRate
            };
        }

        public override void reset()
        {
            base.reset();
            LinkStore = InitialLinkStore;
        }
    }
}

