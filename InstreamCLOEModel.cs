﻿using System;
using System.Collections.Generic;
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

        #region Streambank Erosion terms
        [Input]
        public double Erodibility { get; set; }

        [Input]
        public double Veg { get; set; }

        [Input]
        public double Mgt { get; set; }

        [Parameter]
        public double Alpha { get; set; }

        [Parameter]
        public double O { get; set; }
        #endregion

        public override void runTimeStep(DateTime now, double theTimeStepInSeconds)
        {
            LinkStore += AdditionalInflowMass + CatchmentInflowMass + UpstreamFlowMass;
            LinkStore += BankErosion;

            ProcessedLoad = LinkStore*ConstituentOutFraction;
        }

        public double ConstituentOutFraction
        {
            get
            {
                var exp = -B51 * Link.TravelTime;
                return Dlink + Olink*Math.Exp(exp);
            }
        }

        public double BankErosion
        {
            get
            {
                return O * Alpha * DownstreamFlowVolume * Mgt * Veg * Erodibility * Link.Length;
            }
        }

        public override LinkSourceSinkModel CloneForMultipleDivisions()
        {
            return new InstreamCLOEModel()
            {
                Dlink = Dlink,
                Olink = Olink,
                B51 = B51,
                Erodibility = Erodibility,
                Veg = Veg,
                Mgt = Mgt,
                Alpha = Alpha,
                O = O
            };
        }

        public override void reset()
        {
            base.reset();
            LinkStore = InitialLinkStore;
        }
    }
}

