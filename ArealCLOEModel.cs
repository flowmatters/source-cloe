namespace Source.CLOE
{
    public class ArealCLOEModel : AbstractCLOEFUModel
    {
        public override double GenerateRateKg_timestep
        {
            get { return base.GenerateRateKg_timestep * AreaHa; }
        }

        public double AreaHa
        {
            get { return areaInSquareMeters * 1e-4; }
        }
    }
}