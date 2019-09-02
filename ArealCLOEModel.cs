namespace Source.CLOE
{
    public class ArealCLOEModel : AbstractCLOEFUModel
    {
        public override double GenerateRateKg_S
        {
            get { return base.GenerateRateKg_S * AreaHa; }
        }

        public double AreaHa
        {
            get { return areaInSquareMeters * 1e-4; }
        }
    }
}