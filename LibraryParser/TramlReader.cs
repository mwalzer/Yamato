﻿using NLog;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Linq;

namespace LibraryParser
{
    public class TraMLReader : LibraryReader
    {
        public XNamespace ns = "http://psi.hupo.org/ms/traml";
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private string lastPeptideRead = String.Empty;

        public Library LoadLibrary(string path)
        {
            logger.Info("Loading file: {0}", path);

            Library library = new Library();
            using (XmlReader reader = XmlReader.Create(path))
            {
                while (reader.Read())
                {
                    if (reader.IsStartElement())
                    {
                        switch (reader.LocalName)
                        {
                            case "Protein":
                                AddProtein(library, reader);
                                break;
                            case "ProteinRef":
                                AddPeptideReference(library, reader);
                                break;
                            case "Peptide":
                                lastPeptideRead = reader.GetAttribute("id");
                                AddPeptide(library, reader);
                                break;
                            case "Transition":
                                AddTransition(library, reader);
                                break;
                        }
                    }
                }
                logger.Debug("{0} proteins loaded", library.ProteinList.Count);
                logger.Debug("{0} unique uniprot IDs", library.UniprotIdList.Count);
                logger.Debug("{0} decoy proteins loaded", library.ProteinDecoyList.Count);
                logger.Debug("{0} peptides loaded", library.PeptideList.Count);
                logger.Debug("{0} IRT peptides loaded", library.RtList.Count);
                logger.Debug("{0} transitions loaded", library.TransitionList.Count);
            }
            System.IO.File.WriteAllLines("proteins.txt", library.UniprotIdList.Values.Cast<string>());
            return library;
        }

        private void AddProtein(Library library, XmlReader xmlReader)
        {
            var protein = new Library.Protein();
            protein.Id = xmlReader.GetAttribute("id");
            protein.AssociatedPeptideIds = new List<string>();
            if (protein.Id.StartsWith("DECOY"))
            {
                library.ProteinDecoyList.Add(protein.Id, protein);
            }
            else
            {
                library.ProteinList.Add(protein.Id, protein);
                StoreUniprotIds(library, protein.Id);
            }
        }

        private void AddPeptide(Library library, XmlReader reader)
        {
            var peptide = new Library.Peptide();
            peptide.Id = reader.GetAttribute("id");
            peptide.Sequence = reader.GetAttribute("sequence");
            peptide.AssociatedTransitionIds = new List<string>();
            bool cvParamsRead = false;

            while (reader.Read() && !cvParamsRead)
            {
                if (reader.IsStartElement())
                {
                    if (reader.LocalName == "cvParam")
                    {
                        switch (reader.GetAttribute("accession"))
                        {
                            case "MS:1000041":
                                peptide.ChargeState = Convert.ToInt32(reader.GetAttribute("value"));
                                break;
                            case "MS:1000893":
                                peptide.GroupLabel = reader.GetAttribute("value");
                                break;
                            case "MS:1000896":
                                peptide.RetentionTime = Convert.ToInt32(reader.GetAttribute("value"));
                                break;
                        }
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "Peptide")
                {
                    cvParamsRead = true;
                }
            }

            library.PeptideList.Add(peptide.Id, peptide);
        }

        private void AddPeptideReference(Library library, XmlReader xmlReader)
        {
            string proteinRef = xmlReader.GetAttribute("ref");
            if (!proteinRef.StartsWith("DECOY"))
            {
                Library.Protein correspondingProtein = (Library.Protein)(library.ProteinList[proteinRef]);
                correspondingProtein.AssociatedPeptideIds.Add(lastPeptideRead);
            }
            else
            {
                Library.Protein correspondingProtein = (Library.Protein)(library.ProteinDecoyList[proteinRef]);
                correspondingProtein.AssociatedPeptideIds.Add(lastPeptideRead);
            }
        }

        private void AddTransition(Library library, XmlReader reader)
        {
            var transition = new Library.Transition();
            transition.PeptideId = reader.GetAttribute("peptideRef");
            transition.Id = reader.GetAttribute("id");
            Enums.IonType? ionType = null;
            bool cvParamsRead = false;
            while (reader.Read() && !cvParamsRead)
            {
                if (reader.IsStartElement())
                {
                    if (reader.LocalName == "Precursor")
                        ionType = Enums.IonType.Precursor;
                    else if (reader.LocalName == "Product")
                        ionType = Enums.IonType.Product;

                    if (reader.LocalName == "cvParam")
                    {
                        switch (reader.GetAttribute("accession"))
                        {
                            case "MS:1000827":
                                if (ionType == Enums.IonType.Precursor)
                                    transition.PrecursorMz = Convert.ToDouble(reader.GetAttribute("value"));
                                else if (ionType == Enums.IonType.Product)
                                    transition.ProductMz = Convert.ToDouble(reader.GetAttribute("value"));
                                break;
                            case "MS:1000041":
                                transition.ProductIonChargeState = Convert.ToInt32(reader.GetAttribute("value"));
                                break;
                            case "MS:1000903":
                                transition.ProductIonSeriesOrdinal = Convert.ToInt32(reader.GetAttribute("value"));
                                break;
                            case "MS:1000926":
                                transition.ProductInterpretationRank = Convert.ToInt32(reader.GetAttribute("value"));
                                break;
                            case "MS:1001220":
                                transition.IonType = reader.GetAttribute("value");
                                break;
                            case "MS:1001226":
                                transition.ProductIonIntensity = Convert.ToDouble(reader.GetAttribute("value"));
                                break;
                        }
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "Transition")
                {
                    if (reader.LocalName == "Precursor" || reader.LocalName == "Product")
                        ionType = null;
                    if(reader.LocalName == "Transition")
                        cvParamsRead = true;
                }
            }

            library.TransitionList.Add(transition.Id, transition);
            var correspondingPeptide = (Library.Peptide)(library.PeptideList[transition.PeptideId]);
            correspondingPeptide.AssociatedTransitionIds.Add(transition.Id);
        }
    }
}