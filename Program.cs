using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using DataStructUtils;
using System.Text.RegularExpressions;
using NERA1.TTLService;
using NERA1.DiacService;
using System.Xml;
using SharpEntropy;
using SharpEntropy.IO;

namespace NERA1
{
    struct Score
    {
        public string clas;
        public double score;
    }

    class Program
    {
        static DiacWebService dws = null;
        static TTL ttlws = null;

        //static GisModel roModel;
        static GisModel mModel;

        static void Main(string[] args)
        {
            System.Net.ServicePointManager.Expect100Continue = false;
            bool alreadyProcessed = false;
            bool keepWholeProcessing = false;
            bool diacInsertion = false;
            string inputFile = null;
            string meModelFile = null;

            ttlws = new TTL();
            string lang = "ro";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--input")
                {
                    if (i + 1 < args.Length)
                    {
                        inputFile = args[i + 1];
                    }
                }
                else if (args[i] == "--source")
                {
                    if (i + 1 < args.Length)
                    {
                        lang = args[i + 1].ToLower();
                    }
                }
                else if (args[i] == "--param")
                {
                    if (i + 1 < args.Length && args[i + 1].ToLower() == "k=true")
                    {
                        keepWholeProcessing = true;
                    }
                    else if (i + 1 < args.Length && args[i + 1].ToLower() == "ap=true")
                    {
                        alreadyProcessed = true;
                    }
                    else if (i + 1 < args.Length && args[i + 1].ToLower() == "di=true")
                    {
                        diacInsertion = true;
                    }
                }
            }

            if (inputFile == null)
            {
                Console.WriteLine("Usage: NERA1.exe --input [FILE] [--source [LANG]] [--param [ap:already preprocessed]=[TRUE]/[FALSE]] [--param [di:diacritics insertion]=[TRUE]/[FALSE]] [--param [k:keep preprocessing annotation in the output]=[TRUE]/[FALSE]]");
                Console.WriteLine();
                Console.WriteLine("Default Language: ro");
                Console.WriteLine("Default Already Preprocessed (RACAI's TTL preprocessing) <ap>: false");
                Console.WriteLine("Default Diacritics Insertion <di>: false");
                Console.WriteLine("Default Keep Preprocessing Annotation in the output <k>: false");
            }
            else
            {
                if (!File.Exists("sgmlunic.ent"))
                {
                    Console.WriteLine("Error: file \"sgmlunic.ent\" is missing!");
                }
                else
                {
                    meModelFile = lang + "Model.txt";
                    mModel = readModel(meModelFile);
                    work(inputFile, alreadyProcessed, diacInsertion, keepWholeProcessing, lang);
                }
            }
        }

        private static GisModel readModel(string modelDataFile)
        {
            PlainTextGisModelReader reader = new PlainTextGisModelReader(modelDataFile);
            return (new GisModel(reader));
        }

        private static void work(string inputFile, bool alreadyProcessed, bool diacInsertion, bool keepWholeProcessing, string lang)
        {
            StreamReader rdr = new StreamReader(inputFile, Encoding.UTF8);
            string line = "";
            double total = 0;
            while ((line = rdr.ReadLine()) != null)
            {
                total++;
            }
            rdr.Close();

            rdr = new StreamReader(inputFile, Encoding.UTF8);
            double count = 0;
            while ((line = rdr.ReadLine()) != null)
            {
                count++;
                string[] ioFiles = line.Trim().Split('\t');
                Console.Write("Annotating {0}... ", Path.GetFileName(ioFiles[0]));
                work(ioFiles[0], ioFiles[1], alreadyProcessed, diacInsertion, keepWholeProcessing, lang);
                Console.WriteLine("done\t{0:#.##}%", count / total * 100);
            }
            rdr.Close();
        }

        private static void work(string inputFile, string outputFile, bool alreadyProcessed, bool diacInsertion, bool keepWholeProcessing, string lang)
        {
            List<string> rawSentences = null;
            string pText = null;

            if (!alreadyProcessed)
            {
                string text = DataStructReader.readWholeTextFile(inputFile, Encoding.UTF8);

                if (lang == "ro" && diacInsertion)
                {
                    try
                    {
                        Console.Write("Diacritics insertion... ");
                        dws = new DiacService.DiacWebService();
                        text = dws.ProcessText(text);
                        Console.WriteLine("complete for {0}", inputFile);
                    }
                    catch
                    {
                        Console.WriteLine("NOT possible...");
                    }
                }

                rawSentences = new List<string>();

                Console.WriteLine("Preprocessing {0}... ", inputFile);
                pText = preprocess(text, lang, ref rawSentences);
            }
            else
            {
                pText = DataStructReader.readWholeTextFile(inputFile, Encoding.UTF8);
                rawSentences = extractRawSentences(pText, lang);
            }

            if (rawSentences != null)
            {
                //Console.Write("\nAnnotating NEs... ");
                NER(pText, outputFile, rawSentences, keepWholeProcessing, lang);
                //Console.WriteLine("done");
            }
        }

        private static List<string> extractRawSentences(string pText, string lang)
        {
            pText = pText.Replace("\n", " ");
            List<string> ret = new List<string>();

            Regex regex = new Regex(
                   "<seg lang=\"" + lang + "\">.+?</seg>",
                   RegexOptions.Singleline
                   );

            Match match = regex.Match(pText);
            while (match.Success)
            {
                ret.Add(toRawSentence(match.Value));
                match = match.NextMatch();
            }

            return ret;
        }

        private static string toRawSentence(string xmlText)
        {
            StringBuilder sb = new StringBuilder();
            XmlDocument xdoc = new XmlDocument();
            xdoc.LoadXml("<!DOCTYPE root [<!ENTITY % SGMLUniq SYSTEM \"sgmlunic.ent\"> %SGMLUniq;]>\n<root>" + xmlText.Replace("", "").Replace("\x01", "").Replace("\x08", "").Replace("\x1B", "").Replace("&b.theta;", "&b.Theta;").Replace("&b.phi;", "&b.Phi;") + "</root>");
            XmlNodeList list = xdoc.SelectNodes("//w | //c");
            for (int i = 0; i < list.Count; i++)
            {
                string token = list[i].InnerText;
                sb.Append(token + " ");
            }
            return sb.ToString().Trim();
        }

        private static void NER(string pText, string outputFile, List<string> rawSentences, bool keepWholeProcessing, string lang)
        {
            HashSet<string> allowedPos = new HashSet<string>() { "Np", "Yn", "Ncmsvn", "X", "Spsa" };
            HashSet<string> delicatePos = new HashSet<string>() { "X", "Spsa", "Rw" };

            HashSet<string> commonNes = null;
            HashSet<string> months = null;
            HashSet<string> correctionNes = null;
            HashSet<string> noSpsa = null;
            HashSet<string> locationClues = null;

            if (lang == "ro")
            {
                commonNes = new HashSet<string>() { "papă", "suveran", "britan", "universitate", "agenție", "internațional", "național", "institut" };
                months = new HashSet<string>() { "ianuarie", "februarie", "martie", "aprilie", "mai", "iunie", "iulie", "august", "septembrie", "octombrie", "noiembrie", "decembrie" };
                correctionNes = new HashSet<string>() { "şi", "și", "sau" };
                noSpsa = new HashSet<string>() { "în" };
                locationClues = new HashSet<string>() { "de", "la", "spre", "în" };
            }
            else if (lang == "en")
            {
                commonNes = new HashSet<string>() { "pope", "university", "agency", "international", "national", "institute" };
                months = new HashSet<string>() { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };
                correctionNes = new HashSet<string>() { "and", "or" };
                noSpsa = new HashSet<string>() { "in" };
                locationClues = new HashSet<string>() { "from", "to", "towards", "in" };
            }

            StreamWriter wrt = new StreamWriter(outputFile, false, Encoding.UTF8);
            wrt.AutoFlush = true;

            string neChunk = "";

            string previousToken = "";
            bool previousIsNe = false;
            int datePossible = 0;
            StringBuilder date = new StringBuilder();

            List<List<Token>> neTokens = new List<List<Token>>();
            //string[] sentences = pText.Split('\n');
            Regex regex = new Regex(
                   "<seg lang=\"" + lang + "\">.+?</seg>",
                   RegexOptions.Singleline
                   );

            Match m = regex.Match(pText);

            while (m.Success)
            {
                XmlDocument xdoc = new XmlDocument();
                xdoc.LoadXml("<!DOCTYPE root [<!ENTITY % SGMLUniq SYSTEM \"sgmlunic.ent\"> %SGMLUniq;]>\n<root>" + m.Value.Replace("", "").Replace("\x01", "").Replace("\x08", "").Replace("\x1B", "").Replace("&b.theta;", "&b.Theta;").Replace("&b.phi;", "&b.Phi;") + "</root>");
                XmlNodeList list = xdoc.SelectNodes("//w|//c");
                foreach (XmlNode node in list)
                {
                    if (node.Name == "w")
                    {
                        string pos = "PUNCT";
                        string occurence = node.InnerText;
                        string lemma = occurence;

                        if (node.Attributes["lemma"] != null)
                        {
                            lemma = node.Attributes["lemma"].InnerText.ToLower();
                            if (lemma.Contains(")"))
                            {
                                lemma = lemma.Substring(lemma.IndexOf(")") + 1);
                            }
                            pos = node.Attributes["ana"].InnerText;
                        }


                        if (allowedPos.Contains(pos) && !Regex.Match(lemma, "^\\d{1,2}-\\d{1,2}$").Success)
                        {
                            if ((pos == "Np" || pos == "Yn") && char.IsUpper(occurence[0]))
                            {
                                addNe(ref neTokens, node, previousIsNe);
                                previousIsNe = true;
                                if (node.Attributes["chunk"] != null)
                                {
                                    neChunk = node.Attributes["chunk"].InnerText;
                                }
                            }
                            else if (!noSpsa.Contains(occurence.ToLower()) && (pos != "Np" && pos != "Yn") && (!delicatePos.Contains(pos) || (previousIsNe && delicatePos.Contains(pos))))
                            {
                                addNe(ref neTokens, node, previousIsNe);
                                previousIsNe = true;
                                if (node.Attributes["chunk"] != null)
                                {
                                    neChunk = node.Attributes["chunk"].InnerText;
                                }
                            }
                            else
                            {
                                previousIsNe = false;
                                neChunk = "";
                            }
                        }
                        else if (commonNes.Contains(lemma) && char.IsUpper(occurence[0]) && !correctionNes.Contains(occurence.ToLower()))
                        {
                            addNe(ref neTokens, node, previousIsNe);
                            previousIsNe = true;
                            if (node.Attributes["chunk"] != null)
                            {
                                neChunk = node.Attributes["chunk"].InnerText;
                            }
                        }
                        else if (lemma != "" && char.IsUpper(lemma[0]))
                        {
                            addNe(ref neTokens, node, previousIsNe);
                            previousIsNe = true;
                            if (node.Attributes["chunk"] != null)
                            {
                                neChunk = node.Attributes["chunk"].InnerText;
                            }
                        }
                        else if ((locationClues.Contains(previousToken) || previousIsNe) && char.IsUpper(occurence[0]))
                        {
                            addNe(ref neTokens, node, previousIsNe);
                            previousIsNe = true;
                            if (node.Attributes["chunk"] != null)
                            {
                                neChunk = node.Attributes["chunk"].InnerText;
                            }
                        }
                        else if (previousIsNe && (node.Attributes["chunk"] != null && neChunk == node.Attributes["chunk"].InnerText) && !correctionNes.Contains(occurence.ToLower()))
                        {
                            addNe(ref neTokens, node, previousIsNe);
                            previousIsNe = true;
                            if (node.Attributes["chunk"] != null)
                            {
                                neChunk = node.Attributes["chunk"].InnerText;
                            }
                        }
                        else
                        {
                            previousIsNe = false;
                            neChunk = "";
                        }

                        if (pos.StartsWith("M") || Regex.Match(lemma, "^\\d{1,2}-\\d{1,2}$").Success)
                        {
                            if (datePossible == 0 && (occurence.Length < 3 || occurence.Contains("-")))
                            {
                                datePossible++;
                                date.Append(occurence);
                            }
                            else if (datePossible == 2 && occurence.Length == 4)
                            {
                                date.Append(" " + occurence);
                                addNe(ref neTokens, node, false);
                                datePossible = 0;
                                date = new StringBuilder();
                            }
                            else
                            {
                                datePossible = 0;
                                date = new StringBuilder();
                            }
                        }
                        else if (datePossible == 1 && months.Contains(occurence.ToLower()))
                        {
                            datePossible++;
                            date.Append(" " + occurence);
                        }
                        else
                        {
                            if (datePossible == 2)
                            {
                                addNe(ref neTokens, node, false);
                            }
                            datePossible = 0;
                            date = new StringBuilder();
                        }

                        previousToken = lemma;
                    }
                    else if (node.Name == "c")
                    {
                        previousIsNe = false;
                        previousToken = node.InnerText;
                        neChunk = "";
                        previousToken = "";
                    }
                }
                string final = "";
                List<NamedEntity> nes = toNEandString(neTokens, ref final);

                List<string> rawTokens = new List<string>();
                List<int> positions = new List<int>();
                List<Token> tokens = new List<Token>();

                int position = getRawTokensAtPositions(ref rawTokens, ref positions, ref tokens, list);

                int indexStart = 0;
                //List<int> notGood = new List<int>();

                for (int j = 0; j < nes.Count; j++)
                {
                    indexStart = findForm(ref nes, j, rawTokens, positions, indexStart);
                }

                for (int j = nes.Count - 1; j >= 0; j--)
                {
                    if (nes[j].startIndex != -1)
                    {
                        string features = extractFeatures(nes[j], tokens, true);
                        Score score = predict(features, mModel);
                        nes[j].type = score.clas;
                    }
                    else
                    {
                        nes.RemoveAt(j);
                    }
                }

                string output = toOutput(nes, list, keepWholeProcessing);
                wrt.WriteLine(output);

                neTokens.Clear();

                m = m.NextMatch();
            }

            wrt.Close();

        }

        private static string toOutput(List<NamedEntity> nes, XmlNodeList nodeList, bool keepWholeProcessing)
        {
            HashSet<string> TIMEX = new HashSet<string>() { "TIME", "DATE" };
            StringBuilder sb = new StringBuilder();

            if (nodeList.Count > 0)
            {
                if (keepWholeProcessing)
                {
                    sb.Append("<sent>");
                }
                int i = 0;
                for (int j = 0; j < nodeList.Count; j++)
                {
                    string ne = "ENAMEX";
                    if (i < nes.Count && TIMEX.Contains(nes[i].type))
                    {
                        ne = "TIMEX";
                    }

                    if (i < nes.Count && j == nes[i].startIndex)
                    {
                        sb.Append("<" + ne + " TYPE=\"" + nes[i].type + "\">");
                    }
                    if (!keepWholeProcessing)
                    {
                        sb.Append(nodeList[j].InnerText.Replace("_", " "));
                    }
                    else
                    {
                        sb.Append(nodeList[j].OuterXml);
                    }
                    if (i < nes.Count && j == nes[i].endIndex)
                    {
                        sb.Append("</" + ne + ">");
                        i++;
                    }
                    sb.Append(" ");
                }

                if (keepWholeProcessing)
                {
                    sb.Append("</sent>");
                }
            }
            return sb.ToString().Trim();
        }

        private static List<NamedEntity> toNEandString(List<List<Token>> neTokens, ref string retString)
        {
            List<NamedEntity> ret = new List<NamedEntity>();

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < neTokens.Count; i++)
            {
                string str = toString(neTokens[i]);
                NamedEntity ne = new NamedEntity();
                ne.occurence = str.Trim();
                sb.Append(str);
                ret.Add(ne);
            }
            retString = sb.ToString().Trim();

            return ret;
        }

        private static string toString(List<Token> list)
        {
            HashSet<string> delicatePos = new HashSet<string>() { "X", "Spsa", "Rw" };

            StringBuilder sb = new StringBuilder();
            StringBuilder aux = new StringBuilder();

            for (int i = 0; i < list.Count; i++)
            {
                if (!delicatePos.Contains(list[i].pos))
                {
                    string auxRaw = aux.ToString().Trim();
                    if (auxRaw != "")
                    {
                        sb.Append(auxRaw + " ");
                        aux.Clear();
                    }
                    sb.Append(list[i].occurence + " ");
                }
                else
                {
                    aux.Append(list[i].occurence + " ");
                }
            }
            string ret = sb.ToString().Trim();
            if (ret != "")
            {
                ret = ret + "\n";
            }
            return ret;
        }

        private static Score predict(string context, GisModel mModel)
        {
            double[] probabilities = mModel.Evaluate(context.Split(' '));
            Score score = new Score();
            score.clas = mModel.GetBestOutcome(probabilities);
            score.score = probabilities[mModel.GetOutcomeIndex(score.clas)];

            return score;
        }

        private static int getRawTokensAtPositions(ref List<string> rawTokens, ref List<int> positions, ref List<Token> tokens, XmlNodeList xmlTokens)
        {
            int position = 0;

            foreach (XmlNode xmlToken in xmlTokens)
            {
                string occurence = xmlToken.InnerText.Replace("ș", "ş").Replace("ț", "ţ"); ;
                string lemma = occurence;
                string pos = "_";

                if (xmlToken.Name == "w")
                {
                    lemma = xmlToken.Attributes["lemma"].InnerText.Replace("ș", "ş").Replace("ț", "ţ"); ;
                    pos = xmlToken.Attributes["ana"].InnerText;
                }

                tokens.Add(new Token(occurence, lemma, pos));

                if (occurence.Contains("_"))
                {
                    string[] parts = occurence.Split('_');
                    foreach (string part in parts)
                    {
                        rawTokens.Add(part);
                        positions.Add(position);
                    }
                }
                else
                {
                    rawTokens.Add(occurence);
                    positions.Add(position);
                }

                position++;
            }

            return position;
        }

        private static int findForm(ref List<NamedEntity> entities, int entIdx, List<string> tokens, List<int> positions, int indexStart)
        {
            string entity = entities[entIdx].occurence;

            if (!Regex.Match(entity, "\\d,\\d").Success)
            {
                entity = entity.Replace(",", " , ");
            }
            if (Regex.Match(entity.Replace(" ", ""), "\\d\\.\\d").Success)
            {
                entity = entity.Replace(".", " . ");
            }

            entity = entity.Replace(":", " : ");
            while (entity.Contains("  "))
            {
                entity = entity.Replace("  ", " ");
            }

            string[] entTokens = entity.Split(' ');

            for (int i = indexStart; i < tokens.Count; i++)
            {
                if (entTokens[0] == tokens[i] && matchAt(entTokens, tokens, i))
                {
                    entities[entIdx].startIndex = positions[i];
                    entities[entIdx].endIndex = positions[i + entTokens.Length - 1];
                    return i + entTokens.Length;
                }
            }

            return indexStart;
        }

        private static bool matchAt(string[] entTokens, List<string> tokens, int pos)
        {
            for (int i = 0; i < entTokens.Length; i++)
            {
                if (entTokens[i] != tokens[pos + i])
                {
                    return false;
                }
            }
            return true;
        }

        private static void addNe(ref List<List<Token>> tokens, XmlNode ne, bool previousIsNe)
        {
            string occurence = ne.InnerText;
            string lemma = ne.Attributes["lemma"].InnerText;
            string pos = ne.Attributes["ana"].InnerText;

            Token token = new Token(occurence, lemma, pos);

            if (!previousIsNe || tokens.Count == 0)
            {
                tokens.Add(new List<Token>());
            }

            tokens[tokens.Count - 1].Add(token);
        }

        private static string extractFeatures(NamedEntity namedEntity, List<Token> tokens, bool train)
        {
            StringBuilder featuresArray = new StringBuilder();
            //tokens = reduceTokens(tokens, ref namedEntity);

            //is it First?
            if (namedEntity.startIndex == 0 /*|| tokens[namedEntity.startIndex - 1].pos == "_"*/)
            {
                featuresArray.Append("first ");
            }
            else
            {
                featuresArray.Append("notFirst ");

                Token prevToken = tokens[namedEntity.startIndex - 1];
                featuresArray.Append("prevToken=" + prevToken.lemma + " ");
                featuresArray.Append("prevTokenPos=" + prevToken.pos.Substring(0, 1).ToLower() + " ");

                for (int i = namedEntity.startIndex - 1; i == 0; i--)
                {
                    if (tokens[i].pos.Length > 1 && tokens[i].pos.Substring(0, 2).ToLower() == "vm")
                    {
                        int diff = namedEntity.startIndex - i;
                        featuresArray.Append("prevMainVerb=" + tokens[i].lemma + " ");
                        featuresArray.Append("prevMainVerbDiff=" + diff + " ");
                        break;
                    }
                }
            }

            //is it last?
            if (namedEntity.endIndex == tokens.Count - 1 /*|| tokens[namedEntity.endIndex + 1].pos == "_"*/)
            {
                featuresArray.Append("last ");
            }
            else
            {
                featuresArray.Append("notLast ");

                Token nextToken = tokens[namedEntity.startIndex + 1];
                featuresArray.Append("nextToken=" + nextToken.lemma + " ");
                featuresArray.Append("nextTokenPos=" + nextToken.pos.Substring(0, 1).ToLower() + " ");

                for (int i = namedEntity.endIndex + 1; i < tokens.Count; i++)
                {
                    if (tokens[i].pos.Length > 1 && tokens[i].pos.Substring(0, 2).ToLower() == "vm")
                    {
                        int diff = i - namedEntity.endIndex;
                        featuresArray.Append("nextMainVerb=" + tokens[i].lemma + " ");
                        featuresArray.Append("nextMainVerbDiff=" + diff + " ");
                        break;
                    }
                }
            }

            //non-contextual features
            if (char.IsUpper(namedEntity.occurence[0]))
            {
                featuresArray.Append("firstIsUpper ");
            }
            else
            {
                featuresArray.Append("firstIsLower ");
            }

            if (char.IsDigit(namedEntity.occurence[0]))
            {
                featuresArray.Append("firstIsDigit ");
            }
            else
            {
                featuresArray.Append("firstIsLetter ");
            }

            if (namedEntity.occurence.Contains(" "))
            {
                featuresArray.Append("multi_word ");
            }
            else
            {
                featuresArray.Append("single_word ");
            }

            if (Regex.Match(namedEntity.occurence.Substring(1), "\\d").Success)
            {
                featuresArray.Append("contains_digit ");
            }
            else
            {
                featuresArray.Append("no_digit ");
            }

            if (caps(namedEntity.occurence))
            {
                featuresArray.Append("caps ");
            }
            else
            {
                featuresArray.Append("no_caps ");
            }

            if (consonants(namedEntity.occurence))
            {
                featuresArray.Append("consonants ");
            }
            else
            {
                featuresArray.Append("no_consonants ");
            }

            if (train)
            {
                featuresArray.Append(namedEntity.type);
            }
            return featuresArray.ToString().Trim();
        }

        private static bool caps(string text)
        {
            int totalCaps = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsUpper(text[i]))
                {
                    totalCaps++;
                    if (totalCaps > 1)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool consonants(string text)
        {
            HashSet<string> vowels = new HashSet<string>() { "a", "e", "i", "o", "u", "ă", "î", "â" };

            for (int i = 0; i < text.Length; i++)
            {
                if (vowels.Contains(text.Substring(i, 1).ToLower()))
                    return false;
            }
            return true;
        }

        private static string preprocess(string text, string lang, ref List<string> rawSentences)
        {
            string final = ".!?";
            text = text.Replace("\n", ". ");
            if (text != "")
            {
                text = text.Replace("ș", "ş").Replace("ț", "ţ");
                if (!final.Contains(text.Substring(text.Length - 1)))
                {
                    text = text + ".";
                }
                while (text.Contains("  "))
                {
                    text = text.Replace("  ", " ");
                }
                while (text.Contains(" ."))
                {
                    text = text.Replace(" .", ".");
                }
                while (text.Contains(".."))
                {
                    text = text.Replace("..", ".");
                }
                text = text + "A";
            }
            Regex regexSentences = new Regex(
                ".+?(?<![\\s\\.]\\p{Lu})(?<![\\s\\.]\\p{Lu}[bcdfgjklmnprstvxz" +
                  "])(?<![\\s\\.]\\p{Lu}[bcdfgjklmnprstvxz][bcdfgjklmnprstvxz])" +
                  "[\\.?!]+(?=\\s*[\\p{Lu}\\[\\(\\\"\\'])",
                RegexOptions.None
                );

            Match sentenceMatch = regexSentences.Match(text);

            StringBuilder sb = new StringBuilder();
            List<string> sentences = new List<string>();
            while (sentenceMatch.Success)
            {
                sentences.Add(sentenceMatch.Value.Trim());
                sentenceMatch = sentenceMatch.NextMatch();
            }

            for (int i = 0; i < sentences.Count; i++)
            {
                string sentence = sentences[i];
                rawSentences.Add(sentence);
                string preprocessed = ttlPreprocess(sentence, ttlws, lang);
                Console.Write("\r{0}%   ", 100 * (i + 1) / sentences.Count);
                sb.Append(preprocessed);
            }

            return sb.ToString().Trim().Replace("&", "&amp;");
        }

        private static string ttlPreprocess(string text, TTL ttlServ, string lang)
        {
            try
            {
                text = ttlServ.UTF8toSGML(text);
                text = ttlServ.XCES(lang, lang, text);
                text = ttlServ.SGMLtoUTF7(text);
                ASCIIEncoding asciiEnc = new ASCIIEncoding();
                text = Encoding.UTF8.GetString(UTF7Encoding.Convert(Encoding.UTF7, Encoding.UTF8, asciiEnc.GetBytes(text)));
                return text;
            }
            catch (Exception e)
            {
                return e.ToString();
            }
        }
    }
}