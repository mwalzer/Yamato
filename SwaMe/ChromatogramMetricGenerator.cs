﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using MathNet.Numerics.Interpolation;
using MzmlParser;

namespace SwaMe
{
    class ChromatogramMetricGenerator
    {
        public void GenerateChromatogram(Run run)
        {
            //Interpolation and smoothing to acquire chromatogram metrics
            foreach (BasePeak basepeak in run.BasePeaks)
            {
                double[] intensities = basepeak.Spectrum.Select(x => (double)x.Intensity).ToArray();
                double[] starttimes = basepeak.Spectrum.Select(x => (double)x.RetentionTime).ToArray();

                //if there are less than two datapoints we cannot calculate chromatogrammetrics:
                if (starttimes.Count() < 2)
                    continue;

                //if there are not enough datapoints, interpolate:
                if (starttimes.Count() < 100)
                {
                    RTandInt ri = Interpolate(starttimes, intensities);
                    intensities = ri.intensities;
                    starttimes = ri.starttimes;
                }

                double[,] array3 = new double[1, intensities.Length];
                for (int i = 0; i < intensities.Length; i++)
                {
                    array3[0, i] = intensities[i];
                }

                WaveletLibrary.Matrix dataMatrix = new WaveletLibrary.Matrix(array3);

                var transform = new WaveletLibrary.WaveletTransform(new WaveletLibrary.HaarLift(), 1);
                dataMatrix = transform.DoForward(dataMatrix);

                double[] Smoothedms2bpc = new double[intensities.Length];
                for (int i = 0; i < intensities.Length; i++)
                {
                    Smoothedms2bpc[i] = dataMatrix.toArray[0, i];
                }
                //Find the fwhm:
                double maxIntens = Smoothedms2bpc.Max();
                int mIIndex = Array.IndexOf(Smoothedms2bpc, Smoothedms2bpc.Max());
                double baseline = Smoothedms2bpc.Where(i => i > 0).DefaultIfEmpty(int.MinValue).Min();
                basepeak.FWHM = CalculateFWHM(starttimes, Smoothedms2bpc, maxIntens, mIIndex, baseline);
                double peakTime = starttimes[mIIndex];
                double fwfpct = CalculateFpctHM(starttimes, Smoothedms2bpc, maxIntens, mIIndex, baseline);
                double f = Math.Abs(peakTime - fwfpct);
                basepeak.Peaksym = fwfpct / (2 * f);
                basepeak.PeakCapacity = 1 + (peakTime / basepeak.FWHM);
            }
        }

        public void GenerateiRTChromatogram(Run run, double massTolerance)
        {
            int Count = 0;
            foreach (IRTPeak irtpeak in run.IRTPeaks)
            {
                
                int length = 0;
                double[] RTs = { };
                double highestdp = 0;
                //create the chromatogram for each possible peak
                irtpeak.PossPeaks.OrderByDescending(x => x.BasePeak.Intensity);
                int rank = 1;
                double tempFWHM = 0;
                double tempPeakSym = 0;
                foreach (PossiblePeak pPeak in irtpeak.PossPeaks)
                {
                    SmoothedPeak sp = new SmoothedPeak();
                    sp.peakArea = 0;
                    List<double[]> transSmoothInt = new List<double[]>();
                    double Ti = 0;
                    double tiriPair = 0;
                    double tsquared = 0;
                    double rsquared = 0;
                    double Ri = 0;
                    double tisum = 0;
                    
                    for (int yyy = 0; yyy < pPeak.Alltransitions.Count() - 1; yyy++)
                    {
                        var transitionSpectrum = pPeak.Alltransitions[yyy];
                        double[] intensities = transitionSpectrum.OrderBy(x => x.RetentionTime).Select(x => (double)x.Intensity).ToArray();
                        double[] starttimes = transitionSpectrum.OrderBy(x => x.RetentionTime).Select(x => (double)x.RetentionTime).ToArray();

                        length = intensities.Count();
                        //if there are less than two datapoints we cannot calculate chromatogrammetrics:
                        if (starttimes.Count() < 2)
                            continue;
                        
                        double[,] array3 = new double[1, intensities.Length];
                        for (int i = 0; i < intensities.Length; i++)
                        {
                            array3[0, i] = intensities[i];
                        }

                        WaveletLibrary.Matrix dataMatrix = new WaveletLibrary.Matrix(array3);

                        var transform = new WaveletLibrary.WaveletTransform(new WaveletLibrary.HaarLift(), 1);
                        dataMatrix = transform.DoForward(dataMatrix);

                        double[] Smoothed = new double[intensities.Length];
                        for (int i = 0; i < intensities.Length; i++)
                        {
                            Smoothed[i] = dataMatrix.toArray[0, i];
                        }
                        transSmoothInt.Add(Smoothed);
                        RTs = starttimes;


                        //find all the peaks in the smoothed chromatogram
                        //Collect the peakarea, dotproduct and RT of each of these peaks at peakIndex
                        List<int> peakIndexes = new List<int>();
                        List<int> peakJoint = new List<int>();

                        int mIIndex = Array.IndexOf(Smoothed, Smoothed.Max());
                        double height = Smoothed.Max();
                        double width = starttimes.Max() - starttimes.Min();
                        sp.peakArea = height * width / 2;
                        sp.RT = starttimes[mIIndex];
                        double baseline = Smoothed.Where(i => i > 0).DefaultIfEmpty(int.MinValue).Min();
                        Ri = irtpeak.AssociatedTransitions[yyy].ProductIonIntensity;
                        Ti = transSmoothInt[yyy][length/2];
                        tiriPair = tiriPair + (Ti * Ri);
                        tsquared = tsquared + (Ti * Ti);
                        rsquared = rsquared + (Ri * Ri);
                        tisum = tisum + Ti;

                        tempFWHM = tempFWHM + CalculateFWHM(starttimes, Smoothed, Smoothed.Max(), mIIndex, baseline);
                        double tempFWfpt = CalculateFpctHM(starttimes, Smoothed, Smoothed.Max(), mIIndex, baseline);
                        double f = Math.Abs(irtpeak.RetentionTime - tempFWfpt);
                        tempPeakSym  = tempPeakSym + (tempFWfpt / (2 * f));

                    }
                    sp.DotProduct = Math.Pow(tiriPair, 2) / (tsquared * rsquared);

                    //divide RT into sections:
                    double totalRT = run.Ms1Scans.Last().ScanStartTime - run.Ms1Scans.First().ScanStartTime;
                    double segment = totalRT / run.IRTPeaks.Count();
                    double RTscore = 0;
                    if (Count>0 && Count< run.IRTPeaks.Count()-1 && run.IRTPeaks[Count+1].RetentionTime > sp.RT && run.IRTPeaks[Count - 1].RetentionTime < sp.RT)//if this is not the peaks for the first iRT peptide, we give the peak an RTscore based on whether it is sequential to the last iRT peptide or not
                        RTscore = 1;
                    if (Count==0 && sp.RT!=0)//if this is the first irtpeptide, we want to score its RT based on its proximity to the beginning of the run
                        RTscore =10 * 1/Math.Pow(Math.Abs(sp.RT - 0.227),2);//The power and times ten calculation was added to penalize a peptide greatly for occurring later in the RT. Due to our not wanting to hardcode any peptide standard RTs, we would like to keep the order that the peptides are presented. Therefore, the first peptide should rather occur too early than too late.
                    double score = 0.2 * sp.DotProduct + 0.05 *1/rank*pPeak.Alltransitions[0].Count() + 0.5 * RTscore;//The rank is a rank of the intensity as the peaks were sorted by intensity, the rank is simply the order it occurred in
                    if (highestdp < score)
                    {
                        highestdp = score;
                        irtpeak.RetentionTime = sp.RT;
                        irtpeak.FWHM = tempFWHM / irtpeak.AssociatedTransitions.Count();//average fwhm
                        irtpeak.Peaksym = tempPeakSym / irtpeak.AssociatedTransitions.Count();
                    }
                    rank++;
                }
                Count++;
            }

        }
                 
        public double CalculateFWHM(double[] starttimes, double[] intensities, double maxIntens, int mIIndex, double baseline)
        {
            double halfMax = (maxIntens - baseline) / 2 + baseline;
            double halfRT1 = 0;
            double halfRT2 = 0;
            for (int i = mIIndex; i > 0; i--)
            {
                if (intensities[i] < halfMax)
                {
                    halfRT1 = starttimes[i];
                    break;
                }
                else if (i == 0 && starttimes[i] >= halfMax) { halfRT2 = starttimes[0]; break; }
            }

            for (int i = mIIndex; i < intensities.Length; i++)
            {
                if (intensities[i] < halfMax)
                {
                    halfRT2 = starttimes[i];
                    break;
                }
                else if (i == mIIndex && starttimes[i] >= halfMax) { halfRT2 = starttimes[intensities.Length - 1]; break; }
            }

            return halfRT2 - halfRT1;
        }

        public double CalculateFpctHM(double[] starttimes, double[] intensities, double maxIntens, int mIIndex, double baseline)
        {
            double fiveMax = (maxIntens - baseline) / 20 + baseline;
            double fiveRT1 = 0;
            double fiveRT2 = 0;
            for (int i = mIIndex; i > 0; i--)
            {
                if (intensities[i] < fiveMax)
                {
                    fiveRT1 = starttimes[i];
                    break;
                }
                else if (i == 0 && starttimes[i] >= fiveMax) { fiveRT2 = starttimes[0]; }
            }

            for (int i = mIIndex; i < intensities.Length; i++)
            {
                if (intensities[i] < fiveMax)
                {
                    fiveRT2 = starttimes[i];
                    break;
                }
                else if (i == mIIndex && starttimes[i] >= fiveMax) { fiveRT2 = starttimes[intensities.Length - 1]; }
            }

            return fiveRT2 - fiveRT1;
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
            for (int i = 0; i < numNeeded; i++)
            {
                double placetobe = starttimes[0] + (intervals * i);

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

        public struct RTandInt
        {
            public double[] starttimes;
            public double[] intensities;
        }
    }
}
