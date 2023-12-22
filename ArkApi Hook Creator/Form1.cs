using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TrampolineCreator
{
    public partial class Form1 : Form
    {
        private List<string> _CachedOffsets = new List<string>();

        public Form1()
        {
            InitializeComponent();
            _CachedOffsets = LoadCachedOffsets(ConfigurationManager.AppSettings["CachedOffsetsFilePath"]);
            ModeSelection(ConfigurationManager.AppSettings["DarkMode"]);
        }

        bool LoadingHeaders = false;
        Dictionary<int, Dictionary<string, Dictionary<int, string>>> FunctionInfo = new Dictionary<int, Dictionary<string, Dictionary<int, string>>>();
        Dictionary<string, Dictionary<int, string>> StructureSelector = new Dictionary<string, Dictionary<int, string>>();
        Dictionary<int, string> FunctionSelector = new Dictionary<int, string>();
        Dictionary<int, int> FunctionIndexer = new Dictionary<int, int>();

        private void ModeSelection(string mode)
        {
             if (mode == "true") // Dark Mode
            {
                this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
                this.ForeColor = System.Drawing.Color.Black;

                richTextBox1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
                richTextBox1.ForeColor = System.Drawing.Color.Black;
            }
        }

        private void AddFunction(int ClassIndex, string Structure, int FunctionIndex, string Function)
        {
            if (FunctionInfo.TryGetValue(ClassIndex, out StructureSelector))
            {
                if (StructureSelector.TryGetValue(Structure, out FunctionSelector))
                {
                    if(FunctionSelector.ContainsKey(FunctionIndex))
                        return;
                    FunctionSelector.Add(FunctionIndex, Function);
                }
                else StructureSelector.Add(Structure, new Dictionary<int, string> { { FunctionIndex, Function } });
            }
            else FunctionInfo.Add(ClassIndex, new Dictionary<string, Dictionary<int, string>> { { Structure, new Dictionary<int, string> { { FunctionIndex, Function } } } });
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            GameCombo.SelectedIndex = 0;
        }

        private void GameCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadHeaders();
        }

        private List<string> LoadCachedOffsets(string filename)
        {
            if (filename == null || filename.Length == 0)
            {
                MessageBox.Show("Invalid Cached Offsets File Path detected.\nPlease update the config file with a proper file path.\n\nUnable to continue and will close!", "Cached Offsets File Missing", MessageBoxButtons.OK);
                this.Close();
                Application.Exit();
            }

            var lines = new List<string>();

            try
            {
                using (StreamReader reader = new StreamReader(filename))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lines.Add(line);
                    }
                }
            }
            catch (IOException e)
            {
                Console.WriteLine("An IO exception has been thrown!");
                Console.WriteLine(e.ToString());
                throw e;
            }

            return lines;
        }

        private string FindHookSignature(string searchText)
        {
            string searchValue = $"{searchText}(";
            foreach (string signature in _CachedOffsets)
            {
                if (signature.StartsWith(searchValue))
                    return signature;
            }

            return $"{searchText}()";
        }

        private void LoadHeaders()
        {
            if (LoadingHeaders) return;
            LoadingHeaders = true;
            if (ClassCombo.Items.Count > 0)
            {
                ClassCombo.Items.Clear();
                StructCombo.Items.Clear();
                FuncCombo.Items.Clear();
                FunctionInfo.Clear();
                StructureSelector.Clear();
                FunctionSelector.Clear();
                FunctionIndexer.Clear();
                GameCombo.Enabled = ClassCombo.Enabled = StructCombo.Enabled = false;
            }
            ClassCombo.Items.AddRange(new string[] { "Actor", "GameMode", "Inventory", "Other", "PrimalStructure", "Buff", "UE" });
            int ClassIndex = 0, ClassCount = ClassCombo.Items.Count - 1;
            foreach (string ArkHeader in ClassCombo.Items)
            {
                using (WebClient wc = new WebClient())
                {
                    int ClassId = ClassIndex++;
                    System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                    wc.DownloadStringCompleted += (object s, DownloadStringCompletedEventArgs ea) => ParseArkHeader(ClassId, ClassId == ClassCount, s, ea);
                    wc.DownloadStringAsync(new Uri("https://raw.githubusercontent.com/ServersHub/ServerAPI/main/AsaApi/Core/Public/API/ARK/" + ArkHeader + ".h"));
                }
            }
        }

        private void ParseArkHeader(int ClassIndex, bool Completed, object sender, DownloadStringCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                MessageBox.Show("Error: " + e.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string HtmlData = e.Result.Replace("TWeakObjectPtr<struct ", "TWeakObjectPtr<");
            int FindIndex = -1, StructureIndex = 0, FunctionIndex = 0, indexof = HtmlData.IndexOf("	struct ");
            //Remove structures within structures
            if (indexof != -1)
            {
                int indexofend = HtmlData.IndexOf('}', indexof);
                while(indexof != -1)
                {
                    HtmlData = HtmlData.Remove(indexof, indexofend - indexof + 2);
                    indexof = HtmlData.IndexOf("	struct ");
                    if (indexof != -1) indexofend = HtmlData.IndexOf('}', indexof);
                }
            }
            
            string StructName = "";
            string[] splts = Regex.Split(HtmlData, "struct ");
            for (int i = 1; i < splts.Length; i++)
            {
                //Structure Name
                FindIndex = splts[i].IndexOf("\n");
                if (FindIndex == -1)
                    continue;

                StructName = splts[i].Substring(0, FindIndex);
                if ((FindIndex = StructName.IndexOf(" :")) != -1) StructName = StructName.Substring(0, FindIndex);
                
                //Find Functions
                if ((FindIndex = splts[i].IndexOf("// Functions")) != -1)
                {
                    FindIndex += 15;
                    string[] Functions = splts[i].Substring(FindIndex, splts[i].Length - FindIndex).Split('\n');
                    foreach (string Function in Functions)
                    {
                        if (Function.Contains("//"))
                            continue;
                        if (Function.Length > 5)
                        {

                            AddFunction(ClassIndex, StructName.Replace(" * ", "* ").Replace("__declspec(align(8)) ", ""), FunctionIndex++, Function.Replace("\t", ""));
                        }
                    }
                    FunctionIndex = 0;
                    StructureIndex++;
                }
            }

            if (Completed)
            {
                GameCombo.Enabled = ClassCombo.Enabled = StructCombo.Enabled = true;
                LoadingHeaders = false;
            }
        }

        private void ClassCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            StructCombo.Items.Clear();
            if (FunctionInfo.TryGetValue(ClassCombo.SelectedIndex, out StructureSelector))
                foreach (KeyValuePair<string, Dictionary<int, string>> StructShit in StructureSelector)
                    StructCombo.Items.Add(StructShit.Key);
        }

        private void StructCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            FunctionIndexer.Clear();
            FuncCombo.Items.Clear();
            FuncCombo.Enabled = true;
            FuncCombo.Text = "";
            if (FunctionInfo.TryGetValue(ClassCombo.SelectedIndex, out StructureSelector) && StructureSelector.TryGetValue(StructCombo.Text, out FunctionSelector))
                foreach (KeyValuePair<int, string> func in FunctionSelector)
                {
                    FunctionIndexer.Add(FuncCombo.Items.Count, func.Key);
                    if (func.Value.Contains(" { ")) FuncCombo.Items.Add(Regex.Split(func.Value, " { ")[0].Replace(" * ", "* "));
                    else FuncCombo.Items.Add(func.Value.Replace(" * ", "* "));
                }
        }

        private string LowerCase(string str)
        {
            return string.IsNullOrEmpty(str) ? str : char.IsLower(str, 0) ? (char.ToUpperInvariant(str[0]) + str.Substring(1)) : (char.ToLowerInvariant(str[0]) + str.Substring(1));
        }

        private void FuncCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            richTextBox1.Clear();
            if (FunctionInfo.TryGetValue(ClassCombo.SelectedIndex, out StructureSelector) && StructureSelector.TryGetValue(StructCombo.Text, out FunctionSelector) 
                && FunctionIndexer.TryGetValue(FuncCombo.SelectedIndex, out int FuncIndex) && FunctionSelector.TryGetValue(FuncIndex, out string FunctionData)
                && FunctionData.Contains("NativeCall<")
            )
            {
                //AActor* SpawnActor(UClass* Class, const UE::Math::TVector<double>* Location, const UE::Math::TRotator<double>* Rotation, const FActorSpawnParameters* SpawnParameters)
                //{ return NativeCall<AActor*, UClass*, const UE::Math::TVector<double>*, const UE::Math::TRotator<double>*, const FActorSpawnParameters*>(this, "UWorld.SpawnActor(UClass*,UE::Math::TVector<double>*,UE::Math::TRotator<double>*,FActorSpawnParameters&)", Class, Location, Rotation, SpawnParameters); }
                string[] parsedSpace = Regex.Split(FunctionData, " ").Where(s => s.Length > 0).ToArray();
                string[] parseParenthesis = FunctionData.Split('(');
                var parse = parseParenthesis[0].Split(' ').ToList();
                parse.RemoveAt(parse.Count - 1);
                string retType = string.Join(" ", parse.ToArray());
                string funcName = parseParenthesis[0].Split(' ').Last();
                string args = parseParenthesis[1];
                args = args.Substring(0, args.IndexOf(')')).Replace("this", "_this");

                string[] splitNativecall = Regex.Split(FunctionData, "NativeCall<");
                string[] splitNativeCallParenthesis = splitNativecall[1].Split('(');
                string nativeCallString = splitNativeCallParenthesis[1];
                string[] nativeCallArgs = nativeCallString.Split('"');
                string nativeCall = nativeCallArgs[1] + "(" + splitNativeCallParenthesis[2].Split(')')[0] + ")";
                string hookSignature = FindHookSignature(nativeCallArgs[1]);

                string thisArg = nativeCallString.Contains("this,") ? $"{StructCombo.Text}* _this{(nativeCall.Contains("()") ? "" : ", ")}" : "";
                string hookName = $"Hook_{StructCombo.Text}_{funcName}({thisArg}{args})";
                string[] types = splitNativecall.Last().Split('(');
                string typesFirst = types[0];
                typesFirst = typesFirst.Replace(", TSizedDefaultAllocator<32>", "").Replace("const", "");
                int idx = typesFirst.IndexOf(',');
                typesFirst = typesFirst.Substring(idx + 1, typesFirst.Length - 1 - idx - 1);
                string trampArgs = $"{retType},{(thisArg.Length > 0 ? StructCombo.Text+"*" : "")}{(nativeCall.Contains("()") ? "" : ",")}{(nativeCall.Contains("()") ? "" : typesFirst)}";
                trampArgs = trampArgs.Replace(" ", "").Replace(",TSizedDefaultAllocator<32>", "").Replace("const", "");
                string trampFunc = $"DECLARE_HOOK({StructCombo.Text}_{funcName}, {trampArgs});";
                richTextBox1.AppendText(trampFunc + "\n");
                string retString = retType == "void" ? "" : "return ";
                string hasthis = nativeCallString.Contains("this,") ? (!nativeCall.Contains("()") ? "_this, " : "_this") : "";
                var trampArgsNames = splitNativeCallParenthesis[2].Split(')')[1].Replace("\", ", "").Replace("); }", "");
                if (trampArgsNames.Equals("\""))
                    trampArgsNames = "";
                richTextBox1.AppendText($"{retType} {hookName}\n{{\n    {retString}{StructCombo.Text}_{funcName}_original({hasthis}{trampArgsNames});\n}}\n\n");

                string setHookName = hookName.Split('(')[0];
                string setHookTramp = $"{StructCombo.Text}_{funcName}_original";
                string setHook = $"AsaApi::GetHooks().SetHook(\"{hookSignature}\", &{setHookName}, &{setHookTramp});";
                string disableHook = $"AsaApi::GetHooks().DisableHook(\"{hookSignature}\", &{setHookName});";

                richTextBox1.AppendText(setHook + "\n\n");
                richTextBox1.AppendText(disableHook);

                Clipboard.SetText(richTextBox1.Text);
            }
        }

        private void FuncCombo_TextUpdate(object sender, EventArgs e)
        {
            FuncCombo.Items.Clear();
            FunctionIndexer.Clear();
            if (FunctionInfo.TryGetValue(ClassCombo.SelectedIndex, out StructureSelector) && StructureSelector.TryGetValue(StructCombo.Text, out FunctionSelector))
            {
                string FuncName;
                foreach (KeyValuePair<int, string> func in FunctionSelector)
                {
                    if (func.Value.Contains(" { "))
                    {
                        FuncName = Regex.Split(func.Value, " { ")[0].Replace(" * ", "* ");
                        if (FuncName.ToLower().Contains(FuncCombo.Text.ToLower()))
                        {
                            FunctionIndexer.Add(FuncCombo.Items.Count, func.Key);
                            FuncCombo.Items.Add(FuncName);
                        }
                    }
                    else
                    {
                        FuncName = func.Value.Replace(" * ", "* ");
                        if (FuncName.ToLower().Contains(FuncCombo.Text.ToLower()))
                        {
                            FunctionIndexer.Add(FuncCombo.Items.Count, func.Key);
                            FuncCombo.Items.Add(FuncName);
                        }
                    }
                }
            }
            FuncCombo.SelectionStart = FuncCombo.Text.Length;
            FuncCombo.SelectionLength = 0;
        }
    }
}