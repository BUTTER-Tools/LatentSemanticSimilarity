using System.Text;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Drawing;
using PluginContracts;
using OutputHelperLib;
using System.IO;
using GroupDataObj;


namespace LatentSemanticSimilarity
{
    public class LatentSemanticSimilarity : Plugin
    {


        public string[] InputType { get; } = { "GroupData" };
        public string OutputType { get; } = "OutputArray";

        public Dictionary<int, string> OutputHeaderData { get; set; } = new Dictionary<int, string>() { { 0, "P1" },
                                                                                                        { 1, "P2" },
                                                                                                        { 2, "P1_WordsCaptured" },
                                                                                                        { 3, "P2_WordsCaptured" },
                                                                                                        { 4, "LSS" },
                                                                                                       };
        public bool InheritHeader { get; } = false;

        #region Plugin Details and Info

        public string PluginName { get; } = "Latent Semantic Similarity";
        public string PluginType { get; } = "Dyads & Groups";
        public string PluginVersion { get; } = "1.0.1";
        public string PluginAuthor { get; } = "Ryan L. Boyd (ryan@ryanboyd.io)";
        public string PluginDescription { get; } = "Calculates all pairwise Latent Semantic Similarity (LSS) scores for a group of texts. LSS can be thought of as the degree to which two or more people are talking about the same concepts/content, which is calculated via a pre-trained model. More information on LSS can be found in publications including (but not limited to):" + Environment.NewLine + Environment.NewLine +
             "Babcock, M. J., Ta, V. P., & Ickes, W. (2013). Latent Semantic Similarity and Language Style Matching in Initial Dyadic Interactions. Journal of Language and Social Psychology, 33(1), 78–88. https://doi.org/10.1177/0261927X13499331" + Environment.NewLine + Environment.NewLine +
             "Ta, V. P., Babcock, M. J., & Ickes, W. (2017). Developing Latent Semantic Similarity in Initial, Unstructured Interactions: The Words May Be All You Need. Journal of Language and Social Psychology, 36(2), 143–166. https://doi.org/10.1177/0261927X16638386";
        public string PluginTutorial { get; } = "https://youtu.be/pXwUxEc9hFo";
        public bool TopLevel { get; } = false;


        #endregion

        private double[][] model { get; set; }
        private int TotalNumRows { get; set; }
        private bool modelHasHeader { get; set; }


        public Icon GetPluginIcon
        {
            get
            {
                return Properties.Resources.icon;
            }
        }





        private string InputModelFilename { get; set; } = "";
        private string SelectedEncoding { get; set; } = "utf-8";
        private int VocabSize { get; set; } = 0;
        private int VectorSize { get; set; } = 0;
        private Dictionary<string, int> WordToArrayMap { get; set; }

        TwitterAwareTokenizer tokenizer { get; set; }
        private static string[] stopList { get; } = new string[] { "`", "~", "!", "@", "#", "$", "%", "^", "&", "*", "(",
                                                                    ")", "_", "+", "-", "–", "=", "[", "]", "\\", ";",
                                                                    "'", ",", ".", "/", "{", "}", "|", ":", "\"", "<",
                                                                    ">", "?", "..", "...", "«", "««", "»»", "“", "”",
                                                                    "‘", "‘‘", "’", "’’", "1", "2", "3", "4", "5", "6",
                                                                    "7", "8", "9", "0", "10", "11", "12", "13", "14",
                                                                    "15", "16", "17", "18", "19", "20", "25", "30", "33",
                                                                    "40", "50", "60", "66", "70", "75", "80", "90", "99",
                                                                    "100", "123", "1000", "10000", "12345", "100000", "1000000" };


        public void ChangeSettings()
        {

            using (var form = new SettingsForm_LatentSemanticSimilarity(InputModelFilename, SelectedEncoding, VectorSize, VocabSize))
            {


                form.Icon = Properties.Resources.icon;
                form.Text = PluginName;


                var result = form.ShowDialog();
                if (result == DialogResult.OK)
                {
                    SelectedEncoding = form.SelectedEncoding;
                    InputModelFilename = form.InputFileName;
                    VocabSize = form.VocabSize;
                    VectorSize = form.VectorSize;
                }
            }

        }




        //not used
        public Payload RunPlugin(Payload Input)
        {
            Payload pData = new Payload();
            pData.FileID = Input.FileID;

            for (int i = 0; i < Input.ObjectList.Count; i++)
            {

                GroupData group = (GroupData)(Input.ObjectList[i]);
                string[][] tokens = new string[group.People.Count][];

                //dictionary to track person vectors
                Dictionary<string, double[]> personVectors = new Dictionary<string, double[]>();

                //dictionary to track number of captured words
                Dictionary<string, int> personCapturedWords = new Dictionary<string, int>();

                #region Get Each Person's Mean Vector

                for (int j = 0; j < group.People.Count; j++)
                {

                    //rebuild the person's text into a single unit for analysis
                    StringBuilder personText = new StringBuilder();
                    for (int personTurn = 0; personTurn < group.People[j].text.Count; personTurn++) personText.AppendLine(group.People[j].text[personTurn]);

                    tokens[j] = tokenizer.tokenize(personText.ToString()).Where(x => !stopList.Contains(x)).ToArray();

                    personVectors.Add(group.People[j].id, new double[VectorSize]);
                    //right here is where we need to get the mean vector for each person
                    //int NumberOfDetectedWords = 0;
                    personCapturedWords.Add(group.People[j].id, 0);

                    for (int k = 0; k < VectorSize; k++) personVectors[group.People[j].id][k] = 0;

                    //tally up an average vector for the text
                    #region get mean text vector
                    for (int tokenNumber = 0; tokenNumber < tokens[j].Length; tokenNumber++)
                    {

                        if (WordToArrayMap.ContainsKey(tokens[j][tokenNumber]))
                        {
                            double[] detectedVec = model[WordToArrayMap[tokens[j][tokenNumber]]];
                            personVectors[group.People[j].id] = personVectors[group.People[j].id].Zip(detectedVec, (x, y) => x + y).ToArray();
                            personCapturedWords[group.People[j].id]++;
                        }

                    }

                    if (personCapturedWords[group.People[j].id] > 0)
                    {
                        for (int k = 0; k < VectorSize; k++) personVectors[group.People[j].id][k] = personVectors[group.People[j].id][k] / personCapturedWords[group.People[j].id];
                    }
                    #endregion

                 
                }

                #endregion




                // go in and actually calculate the LSS scores

                #region Calculate LSS

                for (int j = 0; j < group.People.Count - 1; j++)
                {

                    for (int k = 1; k + j < group.People.Count; k++)
                    {



                        int TCpOne = personCapturedWords[group.People[j].id];
                        int TCpTwo = personCapturedWords[group.People[j+k].id];

                        string TextOneID = group.People[j].id;
                        string TextTwoID = group.People[j + k].id;

                        double lssScore = 0;
                        string lssScoreString = "";


                        if (personCapturedWords[TextOneID] > 0 && personCapturedWords[TextTwoID] > 0)
                        {


                            double dotproduct = 0;
                            double d1 = 0;
                            double d2 = 0;

                            //calculate cosine similarity components
                            for (int m = 0; m < VectorSize; m++)
                            {
                                dotproduct += personVectors[TextOneID][m] * personVectors[TextTwoID][m];
                                d1 += personVectors[TextOneID][m] * personVectors[TextOneID][m];
                                d2 += personVectors[TextTwoID][m] * personVectors[TextTwoID][m];
                            }

                            lssScore = (dotproduct / (Math.Sqrt(d1) * Math.Sqrt(d2)));

                            lssScoreString = lssScore.ToString();

                        }



                        pData.StringArrayList.Add(new string[] { TextOneID,
                                                             TextTwoID,
                                                             personCapturedWords[group.People[j].id].ToString(),
                                                             personCapturedWords[group.People[j+k].id].ToString(),
                                                             lssScoreString
                                                            });

                        pData.SegmentNumber.Add(Input.SegmentNumber[i]);
                        pData.SegmentID.Add(TextOneID + ";" + TextTwoID);

                    }

                }
                #endregion



                //OutputArray[0] = Input.StringArrayList[i].Length.ToString();
                //OutputArray[1] = NumberOfDetectedWords.ToString();
                //for (int j = 0; j < VectorSize; j++) OutputArray[j + 2] = textVector[j].ToString();

                //pData.SegmentNumber.Add(Input.SegmentNumber[i]);
                //pData.StringArrayList.Add(OutputArray);

            }

            return (pData);
        }





        public void Initialize()
        {

            
            TotalNumRows = 0;
            string leadingZeroes = "D" + VectorSize.ToString().Length.ToString();


            //we could use a List<double[]> to load in the word vectors, then
            //just .ToArray() it to make jagged arrays. However, I *really* want to avoid
            //having to hold the model in memory twice
            WordToArrayMap = new Dictionary<string, int>();
            if (VocabSize != -1) model = new double[VocabSize][];

            try
            {

           



                #region capture dictionary words and initialize model, if vocabsize is known
                //now, during initialization, we actually go through and want to establish the word group vectors
                using (var stream = File.OpenRead(InputModelFilename))
                using (var reader = new StreamReader(stream, encoding: Encoding.GetEncoding(SelectedEncoding)))
                {

                    if (VocabSize != -1)
                    {
                        string[] firstLine = reader.ReadLine().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    }

                    int WordsFound = 0;

                    while (!reader.EndOfStream)
                    {

                    
                        string line = reader.ReadLine().TrimEnd();
                        string[] splitLine = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        string RowWord = splitLine[0].Trim();
                        double[] RowVector = new double[VectorSize];
                        for (int i = 0; i < VectorSize; i++) RowVector[i] = Double.Parse(splitLine[i + 1]);

                        if (!WordToArrayMap.ContainsKey(RowWord))
                        {
                            WordToArrayMap.Add(RowWord, TotalNumRows);
                            if (VocabSize != -1) model[TotalNumRows] = RowVector;
                        }

                        TotalNumRows++;

                    }
                }


                #endregion



                //if we didn't know the vocab size initially, we know it now that we've walked the whole model
                #region if vocab size was unknown, now we load up the whole model into memory
                if (VocabSize == -1)
                {
                    model = new double[TotalNumRows][];
                    TotalNumRows = 0;

                    //now, during initialization, we actually go through and want to establish the word group vectors
                    using (var stream = File.OpenRead(InputModelFilename))
                    using (var reader = new StreamReader(stream, encoding: Encoding.GetEncoding(SelectedEncoding)))
                    {

                        while (!reader.EndOfStream)
                        {


                            string line = reader.ReadLine().TrimEnd();
                            string[] splitLine = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            string RowWord = splitLine[0].Trim();
                            double[] RowVector = new double[VectorSize];
                            for (int i = 0; i < VectorSize; i++) RowVector[i] = Double.Parse(splitLine[i + 1]);

                            if (WordToArrayMap.ContainsKey(RowWord))
                            {
                                model[TotalNumRows] = RowVector;
                            }

                            TotalNumRows++;

                        }
                    }
                }
                    #endregion





            }
            catch (OutOfMemoryException OOM)
            {
                MessageBox.Show("Plugin Error: Latent Semantic Similarity. This plugin encountered an \"Out of Memory\" error while trying to load your pre-trained model. More than likely, you do not have enough RAM in your computer to hold this model in memory. Consider using a model with a smaller vocabulary or fewer dimensions.", "Plugin Error (Out of Memory)", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            tokenizer = new TwitterAwareTokenizer();

        }





        public bool InspectSettings()
        {

            if (string.IsNullOrEmpty(InputModelFilename))
            {
                return false;
            }
            else
            {
                return true;
            }
            
        }


        public Payload FinishUp(Payload Input)
        {
            Array.Clear(model, 0, model.Length);
            return (Input);
        }


        #region Import/Export Settings
        public void ImportSettings(Dictionary<string, string> SettingsDict)
        {
            InputModelFilename = SettingsDict["InputModelFilename"];
            SelectedEncoding = SettingsDict["SelectedEncoding"];
            VocabSize = int.Parse(SettingsDict["VocabSize"]);
            VectorSize = int.Parse(SettingsDict["VectorSize"]);
        }

        public Dictionary<string, string> ExportSettings(bool suppressWarnings)
        {
            Dictionary<string, string> SettingsDict = new Dictionary<string, string>();
            SettingsDict.Add("InputModelFilename", InputModelFilename);
            SettingsDict.Add("SelectedEncoding", SelectedEncoding);
            SettingsDict.Add("VocabSize", VocabSize.ToString());
            SettingsDict.Add("VectorSize", VectorSize.ToString());

            return (SettingsDict);
        }
        #endregion

    }
}
