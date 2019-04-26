using MathNet.Numerics.Interpolation;
using MzmlParser;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SwaMe
{
    public class MetricGenerator
    {
        public struct RTandInt
        {
            public double[] starttimes;
            public double[] intensities;
        }

        public void GenerateMetrics(Run run, int division)
        {

            //Acquire RTDuration:
            double RTDuration = run.BasePeaks[run.BasePeaks.Count() - 1].RetentionTime - run.BasePeaks[0].RetentionTime;
            double RTsegment = RTDuration / division;
            double[] RTsegs = new double[division];

            for (int uuu = 0; uuu < division; uuu++)
            {
                RTsegs[uuu] = run.BasePeaks[0].RetentionTime + RTsegment * uuu;
            }
            //dividing basepeaks into segments of RT
            foreach (BasePeak basepeak in run.BasePeaks)
            {
                //Check to see in which RTsegment this basepeak is:
                for (int segmentboundary = 1; segmentboundary < RTsegs.Count(); segmentboundary++)
                {
                    if (basepeak.RetentionTime < RTsegs[0]) basepeak.RTsegment = 0;
                    if (basepeak.RetentionTime > RTsegs[segmentboundary - 1] && basepeak.RetentionTime < RTsegs[segmentboundary])
                    {
                        basepeak.RTsegment = segmentboundary;
                    }
                }

                double[] intensities = new double[basepeak.Spectrum.Count()];
                double[] starttimes = new double[basepeak.Spectrum.Count()];
                for (int iii = 0; iii < basepeak.Spectrum.Count(); iii++)
                {
                    intensities[iii] = basepeak.Spectrum[iii].Intensity;
                    starttimes[iii] = basepeak.Spectrum[iii].RetentionTime;
                }

                //if there are less than two datapoints we cannot calculate chromatogrammetrics:
                if (starttimes.Count() < 2) { continue; }

                //if there are not enough datapoints, interpolate:
                if (starttimes.Count() < 100)
                {
                    RTandInt ri = Interpolate(starttimes, intensities);
                    intensities = ri.intensities;
                    starttimes = ri.starttimes;
                }

                double[,] array3 = new double[1, intensities.Length];
                for (int aaa = 0; aaa < intensities.Length; aaa++)
                {
                    array3[0, aaa] = intensities[aaa];
                }

                WaveletLibrary.Matrix dataMatrix = new WaveletLibrary.Matrix(array3);


                var transform = new WaveletLibrary.WaveletTransform(new WaveletLibrary.HaarLift(), 1);
                dataMatrix = transform.DoForward(dataMatrix);

                double[] Smoothedms2bpc = new double[intensities.Length];
                for (int aaa = 0; aaa < intensities.Length; aaa++)
                {
                    Smoothedms2bpc[aaa] = dataMatrix.toArray[0, aaa];
                }
                //Find the fwhm:
                double maxIntens = Smoothedms2bpc.Max();
                int mIIndex = Array.IndexOf(Smoothedms2bpc, Smoothedms2bpc.Max());
                double baseline = Smoothedms2bpc.Where(i => i > 0).DefaultIfEmpty(int.MinValue).Min();
                ChromatogramMetrics cm = new ChromatogramMetrics { };
                basepeak.FWHM = cm.CalculateFWHM(starttimes, Smoothedms2bpc, maxIntens, mIIndex, baseline);
                double peakTime = starttimes[mIIndex];
                double fwfpct = cm.CalculateFpctHM(starttimes, Smoothedms2bpc, maxIntens, mIIndex, baseline);
                double f = Math.Abs(peakTime - fwfpct);
                basepeak.peaksym = fwfpct / (2 * f);
                basepeak.peakCapacity = 1 + (peakTime / basepeak.FWHM);
            }


            //dividing ms1scans into segments of RT
            foreach (Scan scan in run.Ms1Scans)
            {
                //Check to see in which RTsegment this basepeak is:
                for (int segmentboundary = 1; segmentboundary < RTsegs.Count(); segmentboundary++)
                {
                    if (scan.ScanStartTime < RTsegs[0]) scan.RTsegment = 0;
                    if (scan.ScanStartTime > RTsegs[segmentboundary - 1] && scan.ScanStartTime < RTsegs[segmentboundary])
                    {
                        scan.RTsegment = segmentboundary;
                    }
                }


            }


            //dividing ms2scans into segments of RT and calculating swathsize in the process
            double largestSwath = 0;
            double smallestSwath = 400;
            foreach (Scan scan in run.Ms2Scans)
            {
                double swathsize = scan.IsolationWindowTargetMz + scan.IsolationWindowUpperOffset - (scan.IsolationWindowTargetMz - scan.IsolationWindowLowerOffset);

                if (swathsize > largestSwath) { largestSwath = swathsize; continue; }
                if (swathsize < smallestSwath) { smallestSwath = swathsize; }
                //if the scan starttime falls into the rtsegment, give it the correct rtsegment number
                for (int segmentboundary = 1; segmentboundary < RTsegs.Count(); segmentboundary++)
                {
                    if (scan.ScanStartTime < RTsegs[0]) { scan.RTsegment = 0; break; }
                    else if (scan.ScanStartTime > RTsegs[segmentboundary - 1] && scan.ScanStartTime < RTsegs[segmentboundary])
                    {
                        scan.RTsegment = segmentboundary;
                        break;
                    }
                    else if (scan.ScanStartTime > RTsegs[segmentboundary] && segmentboundary == RTsegs.Count()) { scan.RTsegment = segmentboundary + 1; break; }
                }


            }

            double swathSizeDifference = largestSwath - smallestSwath;

            //Retrieveing num OfSwathscans
            divideBySwath Sd = new divideBySwath { };
            int maxswath = Sd.SwathDivider(run);

            //Retrieving cycletimesmetrics
            var CycleTimes = run.Ms2Scans.GroupBy(s => s.Cycle).Select(g => g.OrderByDescending(d => d.ScanStartTime))
            .Select(e => e.First().ScanStartTime - e.Last().ScanStartTime)
            .ToList();

            //Retrieving Density metrics
            var Density = run.Ms2Scans.OrderBy(g => g.Density).Select(g => g.Density).ToList();
            //Create IQR so you can calculate IQR:
            IQR iqr = new IQR { };

            //Retrieve TICChange metrics and divide into rtsegs
            List<double> TICchange50List = new List<double>();
            List<double> TICchangeIQRList = new List<double>();
            var TempTIC = run.Ms2Scans.GroupBy(x => x.RTsegment).Select(d => d.OrderBy(x => x.ScanStartTime).Select(g => g.TotalIonCurrent).ToList());
            for (int eee = 0; eee < TempTIC.Count(); eee++)
            {
                var Temp = TempTIC.ElementAt(eee);
                List<double> Templist = new List<double>();
                for (int uuu = 1; uuu < Temp.Count(); uuu++)
                {

                    Templist.Add(Math.Abs(Temp.ElementAt(uuu) - Temp.ElementAt(uuu - 1)));
                }
                Templist.Sort();
                TICchange50List.Add(Templist.Average());
                TICchangeIQRList.Add(iqr.calcIQR(Templist, Templist.Count()));
            }

            RTDivider Rd = new RTDivider { };
            Rd.DivideByRT(run, division, TICchange50List, TICchangeIQRList);
            UndividedMetrics Um = new UndividedMetrics { };
            
            Um.UndividedMetrix(run, RTDuration, swathSizeDifference, run.Ms2Scans.Count(), maxswath, CycleTimes[CycleTimes.Count() / 2],iqr.calcIQR(CycleTimes, CycleTimes.Count() - 1), Density[Density.Count() / 2], iqr.calcIQR(Density, Density.Count() - 1), run.Ms1Scans.Count());
        }

        private RTandInt Interpolate(double[] starttimes, double[] intensities)
        {
            List<double> intensityList = new List<double>();
            List<double> starttimesList = new List<double>();

            //First step is interpolating a spline, then choosing the pointsat which to add values:
            CubicSpline interpolation = CubicSpline.InterpolateNatural(starttimes, intensities);

            //Now we work out how many to add so we reach at least 100 datapoints and add them:
            double stimesinterval = starttimes.Last() - starttimes[0];
            int numNeeded = 100 - starttimes.Count();
            double intervals = stimesinterval / numNeeded;
            intensityList = intensities.OfType<double>().ToList();
            starttimesList = starttimes.OfType<double>().ToList();
            for (int uuu = 0; uuu < numNeeded; uuu++)
            {
                double placetobe = starttimes[0] + (intervals * uuu);

                //insert newIntensity into the correct spot in the array
                for (int currentintensity = 0; currentintensity < 100; currentintensity++)
                {
                    if (starttimesList[currentintensity] < placetobe) { continue; }
                    else
                    {

                        if (currentintensity > 0)
                        {
                            if (starttimesList[currentintensity] == placetobe) { placetobe = placetobe + 0.01; }
                            double newIntensity = interpolation.Interpolate(placetobe);
                            intensityList.Insert(currentintensity, newIntensity);
                            starttimesList.Insert(currentintensity, placetobe);
                        }
                        else
                        {
                            if (starttimesList[currentintensity] == placetobe) { placetobe = placetobe - 0.01; }
                            double newIntensity = interpolation.Interpolate(placetobe);
                            intensityList.Insert(currentintensity, newIntensity);
                            starttimesList.Insert(currentintensity, placetobe);
                        }

                        break;
                    }
                }
            }
            RTandInt ri = new RTandInt();
            ri.intensities = intensityList.Select(item => Convert.ToDouble(item)).ToArray();
            ri.starttimes = starttimesList.Select(item => Convert.ToDouble(item)).ToArray();

            return ri;

        }

        

    }
}





