﻿using System.Collections.Generic;

namespace SwaMe
{
    public class iRTMetrics
    {
        public List<double> Peakwidths;
        public List<double> PeakSymmetry;
        public iRTMetrics(List<double> Peakwidths, List<double> PeakSymmetry)
        {
            this.Peakwidths = Peakwidths;
            this.PeakSymmetry = PeakSymmetry;
        }
    }
}




