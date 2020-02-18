using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Source.CLOE
{
    sealed class CloeUtils
    {
        public static double safeInv(double x)
        {
            const double TINY = 1e-8;
            if (Math.Abs(x) < TINY)
            {
                var sign = Math.Sign(x);
                x = ((sign==0)?1:sign)*TINY;
            }
            return 1/x;
        }

        public static double weight(double d, double o, double e)
        {
            return (1 - o)*d + o * e;
        }
    }
}
