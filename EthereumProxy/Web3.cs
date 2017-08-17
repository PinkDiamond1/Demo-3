﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Linq;
using static Auctus.EthereumProxy.Solc;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Threading;
using System.Globalization;
using System.Numerics;

namespace Auctus.EthereumProxy
{
    internal class Web3 : ConsoleCommand
    {
        internal enum VariableType { Address, Number, BigNumber, Text, Bool, Null }

        private static KeyValuePair<string, string> MAIN_ADDRESS;

        //WINDOWS
        //private static string KEYSTORE_PATH = "C:\\Users\\Dell\\AppData\\Roaming\\Ethereum\\keystore"; //Main
        //private static string KEYSTORE_PATH = "C:\\Users\\Dell\\AppData\\Roaming\\Ethereum\\testnet\\keystore"; //Tesnet
        //private const string KEYSTORE_PATH = "C:\\Users\\Dell\\AppData\\Local\\Temp\\ethereum_dev_mode\\keystore"; //Dev
        //LINUX
        //private static string KEYSTORE_PATH = "/home/ubuntu/.ethereum/keystore"; //Main
        private static string KEYSTORE_PATH = Util.Config.KeystorePath; //Tesnet
        private const int MAX_CHAR_STAND_INPUT_WRITE = 4096;
        private const int ETHEREUM_DECIMAL = 18;
        private const string DOUBLE_FIXED_POINT = "########################################0.##########################################################################################";

        private static readonly Regex ONLY_NUMBERS = new Regex("^[0-9]+$", RegexOptions.Compiled);
        private static readonly Regex IS_ADDRESS = new Regex("^(0x)?[0-9a-fA-F]{40}$", RegexOptions.Compiled);
        
        private DeployData DeployInfo { get; set; }
        private TransactionData TransactionInfo { get; set; }
        private BigVariableData ABIVariableInfo { get; set; }
        private BigVariableData BinaryVariableInfo { get; set; }

        private Web3() { }

        #region External Methods
        internal static void InitializeMainAddress(string address, string password)
        {
            MAIN_ADDRESS = GetSourceAddres(new KeyValuePair<string, string>(address, password));
        }

        internal static Wallet CreateAccount(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Contains(" "))
                throw new ArgumentException("Invalid password.", "password");

            ConsoleOutput output = new Web3().Execute(string.Format("personal.newAccount(\"{0}\")", password));
            if (!output.Ok || !IS_ADDRESS.IsMatch(output.Output))
                return null;
            
            Wallet account = new Wallet();
            account.Address = output.Output;
            string completePath = Directory.EnumerateFiles(KEYSTORE_PATH).Where(c => c.Split(new string[] { "Z--" }, StringSplitOptions.RemoveEmptyEntries).Last() == account.Address.Substring(2)).Single();
            account.File = File.ReadAllBytes(completePath);
            account.FileName = completePath.Split('\\').Last();
            return account;
        }

        internal static string DeployContract(SCCompiled sc, int gasLimit, int gweiGasPrice, KeyValuePair<string, string> ownerAddressPassword = default(KeyValuePair<string, string>))
        {
            if (sc == null || string.IsNullOrEmpty(sc.Name) || (string.IsNullOrEmpty(sc.Binary) && (string.IsNullOrEmpty(sc.ABI) || sc.ABI == "[]")))
                throw new ArgumentException("Invalid compiled smart contract.", "sc");

            ValidateGasValues(gasLimit, gweiGasPrice);
            ownerAddressPassword = GetSourceAddres(ownerAddressPassword);
            
            return new Web3().deployContract(sc, gasLimit, gweiGasPrice, ownerAddressPassword);
        }

        internal static string Send(string to, BigNumber value, int gasLimit, int gweiGasPrice, KeyValuePair<string, string> fromAddressPassword = default(KeyValuePair<string, string>))
        {
            ValidateAddress(to);
            ValidateGasValues(gasLimit, gweiGasPrice);
            string bigNumber = GetBigNumberFormatted(value);
            fromAddressPassword = GetSourceAddres(fromAddressPassword);
            
            return new Web3().send(to, bigNumber, gasLimit, gweiGasPrice, fromAddressPassword);
        }

        internal static Variable CallConstFunction(CompleteVariableType returnType, string scAddress, string abi, string functionName, params Variable[] parameters)
        {
            if (returnType == null)
                throw new ArgumentNullException("Return type must be filled.", "returnType");
            ValidateContractData(scAddress, abi, functionName);
            return new Web3().callConstFunction(returnType, scAddress, abi, functionName, parameters);
        }

        internal static string CallFunction(string scAddress, string abi, string functionName, BigNumber value, int gasLimit, int gweiGasPrice, KeyValuePair<string, string> fromAddressPassword = default(KeyValuePair<string, string>), params Variable[] parameters)
        {
            ValidateContractData(scAddress, abi, functionName);
            ValidateGasValues(gasLimit, gweiGasPrice);
            string bigNumber = GetBigNumberFormatted(value);
            fromAddressPassword = GetSourceAddres(fromAddressPassword);

            return new Web3().callFunction(scAddress, abi, functionName, bigNumber, gasLimit, gweiGasPrice, fromAddressPassword, parameters);
        }

        internal static double GetBalance(string address)
        {
            ValidateAddress(address);
            ConsoleOutput output = new Web3().Execute(string.Format("eth.getBalance(\"{0}\").toString(10)", address));
            if (!output.Ok)
                throw new Exception("Error to read balance.");
            return GetBigNumberStringToDouble(output.Output, ETHEREUM_DECIMAL);
        }

        internal static List<Event> ReadEvent(string scAddress, string eventName, List<CompleteVariableType> eventParameters, params EventParameter[] filterParameters)
        {
            ValidateAddress(scAddress);
            if (string.IsNullOrEmpty(eventName))
                throw new ArgumentNullException("Event name must be filled.", "eventName");
            if ((eventParameters == null && filterParameters != null && filterParameters.Length > 0) ||
                (eventParameters != null && filterParameters != null && eventParameters.Where(c => c.Indexed).Count() < filterParameters.Length))
                throw new ArgumentException("Filter parameters must match with indexed event parameters.");
            
            return new Web3().readEventFunction(scAddress, eventName, eventParameters, filterParameters);
        }

        internal static BigNumber ETHER(double value)
        {
            return new BigNumber(value, ETHEREUM_DECIMAL);
        }
        
        internal static double GetBigNumberStringToDouble(string bigNumber, int decimals)
        {
            if (string.IsNullOrEmpty(bigNumber) || !ONLY_NUMBERS.IsMatch(bigNumber))
                throw new Exception("Invalid big number.");
            if (decimals < 0)
                throw new Exception("Invalid decimals.");

            string strNonDecimals = "0";
            string strDecimals = "0";
            if (decimals == 0)
                strNonDecimals = bigNumber;
            else if (bigNumber.Length <= decimals)
                strDecimals = bigNumber.PadRight(decimals, '0');
            else
            {
                strNonDecimals = bigNumber.Substring(0, bigNumber.Length - decimals);
                strDecimals = bigNumber.Substring(bigNumber.Length - decimals);
            }
            return double.Parse(string.Format("{0}.{1}", strNonDecimals, strDecimals), CultureInfo.InvariantCulture);
        }

        internal static Transaction GetTransaction(string transactionHash, int amountAttempts = 3, int waitingTime = 5000)
        {
            string transactionData = GetTransactionCompleted(transactionHash, amountAttempts, waitingTime);
            if (!string.IsNullOrEmpty(transactionData))
                return Newtonsoft.Json.JsonConvert.DeserializeObject<Transaction>(transactionData);
            else
                return null;
        }

        internal static CompleteVariableType EtherType
        {
            get { return new CompleteVariableType(VariableType.BigNumber); }
        }
        #endregion
        #region Override Methods
        protected override string getFileName()
        {
            return IS_WINDOWS ? Util.Config.WindowsGethExec : Util.Config.LinuxGethExec;
        }

        protected override string getWorkingDirectory()
        {
            return IS_WINDOWS ? Util.Config.WindowsGethWorkingDir : Util.Config.LinuxGethWorkingDir;
        }

        protected override string getStartArgument(Command command)
        {
            return "attach "+Util.Config.IpcPath;
        }

        protected override string getFormattedComand(string command)
        {
            return command;
        }

        protected override ConsoleOutput executeProcess(Process process, Command command)
        {
            string startOutput = readOutputStream(process.StandardOutput);
            if (!startOutput.StartsWith("Welcome to the Geth JavaScript console!"))
                throw new Exception(string.Format("Error to attach geth.\n\n{0}", startOutput));
            else
                return processCommand(process, command);
        }

        protected override void writeCommand(Process process, Command command)
        {
            process.StandardInput.WriteLine(getValidCommand(command));
        }

        protected override ConsoleOutput readOutput(Process process)
        {
            return new ConsoleOutput()
            {
                Success = true,
                Output = readOutputStream(process.StandardOutput)
            };
        }
        #endregion
        #region Private Methods
        #region Executes
        private string getTransaction(string transactionHash)
        {
            ConsoleOutput output = Execute(string.Format("eth.getTransactionReceipt(\"{0}\")", transactionHash));
            if (output.Ok && output.Output != "null")
            {
                if (output.Output.Contains("transactionHash"))
                    return output.Output;
                else
                    throw new Exception(string.Format("Invalid transaction data.\n\n{0}", output.Output));
            }
            else
                return null;
        }

        private string deployContract(SCCompiled sc, int gasLimit, int gweiGasPrice, KeyValuePair<string, string> ownerAddressPassword)
        {
            DeployInfo = new DeployData()
            {
                ABI = sc.ABI,
                Address = ownerAddressPassword.Key,
                Binary = sc.Binary,
                GasLimit = gasLimit,
                GasPrice = GetBigNumberFormatted(new BigNumber(gweiGasPrice, 9)),
                Name = sc.Name
            };
            ABIVariableInfo = new BigVariableData()
            {
                BaseName = string.Format("abi{0}", DeployInfo.Name),
                RemainingString = DeployInfo.ABI,
                SplitCounter = 0,
                VariableNames = new List<string>(),
                NextFunction = setBinaryBigVariableCommand
            };
            BinaryVariableInfo = new BigVariableData()
            {
                BaseName = string.Format("bin{0}", DeployInfo.Name),
                RemainingString = string.Format("0x{0}", DeployInfo.Binary),
                SplitCounter = 0,
                VariableNames = new List<string>(),
                NextFunction = deployCommand
            };
            ConsoleOutput output = Execute(getUnlockAccountCommand(ownerAddressPassword.Key, ownerAddressPassword.Value, setABIBigVariableCommand));
            if (!output.Ok || !output.Output.Contains("transactionHash"))
                throw new Exception(string.Format("Error to deploy contract.\n\n{0}", output.Output));

            return output.Output.Split(new string[] { "transactionHash:" }, StringSplitOptions.RemoveEmptyEntries).Last().Trim(' ', '\n', '\r', '}', '"');
        }

        private string send(string to, string value, int gasLimit, int gweiGasPrice, KeyValuePair<string, string> fromAddressPassword)
        {
            TransactionInfo = new TransactionData()
            {
                To = to,
                From = fromAddressPassword.Key,
                Value = value,
                GasLimit = gasLimit,
                GasPrice = GetBigNumberFormatted(new BigNumber(gweiGasPrice, 9))
            };
            ConsoleOutput output = Execute(getUnlockAccountCommand(fromAddressPassword.Key, fromAddressPassword.Value, sendCommand));
            if (!output.Ok || !output.Output.StartsWith("0x"))
                throw new Exception(string.Format("Error on send transaction.\n\n{0}", output.Output));

            return output.Output;
        }

        private string callFunction(string scAddress, string abi, string functionName, string value, int gasLimit, int gweiGasPrice, KeyValuePair<string, string> fromAddressPassword, params Variable[] parameters)
        {
            TransactionInfo = new TransactionData()
            {
                To = scAddress,
                From = fromAddressPassword.Key,
                Value = value,
                GasLimit = gasLimit,
                GasPrice = GetBigNumberFormatted(new BigNumber(gweiGasPrice, 9)),
                Function = functionName,
                Parameters = GetParameterListFormatted(parameters)
            };
            ABIVariableInfo = new BigVariableData()
            {
                BaseName = string.Format("abi{0}", functionName),
                RemainingString = abi,
                SplitCounter = 0,
                VariableNames = new List<string>(),
                NextFunction = callFunctionCommand
            };
            ConsoleOutput output = Execute(getUnlockAccountCommand(fromAddressPassword.Key, fromAddressPassword.Value, setABIBigVariableCommand));
            if (!output.Ok || !output.Output.StartsWith("0x"))
                throw new Exception(string.Format("Error on call transaction function.\n\n{0}", output.Output));

            return output.Output;
        }

        private Variable callConstFunction(CompleteVariableType returnType, string scAddress, string abi, string functionName, params Variable[] parameters)
        {
            TransactionInfo = new TransactionData()
            {
                To = scAddress,
                Function = functionName,
                Parameters = GetParameterListFormatted(parameters)
            };
            ABIVariableInfo = new BigVariableData()
            {
                BaseName = string.Format("abi{0}", functionName),
                RemainingString = abi,
                SplitCounter = 0,
                VariableNames = new List<string>(),
                NextFunction = callConstFunctionCommand
            };
            ConsoleOutput output = Execute(setABIBigVariableCommand(new ConsoleOutput() { Success = true, Output = "Start" }));
            if (!output.Ok)
                throw new Exception(string.Format("Error on call const function.\n\n{0}", output.Output));

            return parseStringToVariable(output.Output, returnType.Type, returnType.Decimals);
        }

        private List<Event> readEventFunction(string scAddress, string eventName, List<CompleteVariableType> eventParameters, params EventParameter[] parameters)
        {
            string formattedParameters = GetParameterListFormatted(parameters);
            if (!string.IsNullOrEmpty(formattedParameters))
                formattedParameters = "," + formattedParameters;

            string eventParameterTypes = eventParameters == null || eventParameters.Count == 0 ? "" : string.Join(",", eventParameters.Select(c => c.getTypeDescription()));

            ConsoleOutput output = Execute(string.Format("eth.filter({{ \"fromBlock\": \"0x0\", \"toBlock\": \"latest\", \"address\": \"{0}\", \"topics\": [ web3.sha3(\"{1}({2})\"){3} ] }}).get(function(error,result) {{ error ? console.log(\"Error: \" + error) : console.log(JSON.stringify(result)); }})",
                                            scAddress, eventName, eventParameterTypes, formattedParameters));
            if (!output.Ok || output.Output.StartsWith("Error"))
                throw new Exception(string.Format("Error on read event.\n\n{0}", output.Output));

            string result = output.Output.Split(new string[] { "callbacks:" }, StringSplitOptions.RemoveEmptyEntries).First().TrimEnd('\n', '\r', '{', ' ');
            return parseEventResult(result, eventParameters);
        }
        #endregion
        #region 
        private Command callConstFunctionCommand(ConsoleOutput output)
        {
            return baseCommand(string.Format("eth.contract(JSON.parse(abi{0})).at(\"{1}\").{0}.call({2})", TransactionInfo.Function, TransactionInfo.To, TransactionInfo.Parameters), output);
        }

        private Command callFunctionCommand(ConsoleOutput output)
        {
            return baseCommand(string.Format("eth.contract(JSON.parse(abi{0})).at(\"{1}\").{0}.sendTransaction({2}{{ \"from\": \"{3}\", \"value\": {4}, \"gas\": {5}, \"gasprice\": {6} }})",
                                TransactionInfo.Function, TransactionInfo.To, TransactionInfo.Parameters + (string.IsNullOrEmpty(TransactionInfo.Parameters) ? "" : ","),
                                TransactionInfo.From, TransactionInfo.Value, TransactionInfo.GasLimit, TransactionInfo.GasPrice), output);
        }

        private Command sendCommand(ConsoleOutput output)
        {
            validateAccountUnlocked(output);

            return new Command()
            {
                Comm = string.Format("eth.sendTransaction({{ \"from\": \"{0}\", \"to\": \"{1}\", \"value\": {2}, \"gas\": {3}, \"gasprice\": {4}, \"data\":\"0x\" }})",
                                        TransactionInfo.From, TransactionInfo.To, TransactionInfo.Value, TransactionInfo.GasLimit, TransactionInfo.GasPrice)
            };
        }
        
        private Command setABIBigVariableCommand(ConsoleOutput output)
        {
            if (!output.Ok)
                throw new Exception(string.Format("Error: {0}", output.Output));

            return processBigVariableCommand(ABIVariableInfo, setABIBigVariableCommand);
        }

        private Command setBinaryBigVariableCommand(ConsoleOutput output)
        {
            if (!output.Ok)
                throw new Exception(string.Format("Error: {0}", output.Output));

            return processBigVariableCommand(BinaryVariableInfo, setBinaryBigVariableCommand);
        }
        
        private Command deployCommand(ConsoleOutput output)
        {
            return baseCommand(string.Format("eth.contract(JSON.parse(abi{0})).new(\"{0}\", {{ \"from\": \"{1}\", \"data\": bin{0}, \"gas\": {2}, \"gasprice\": {3} }})",
                                        DeployInfo.Name, DeployInfo.Address, DeployInfo.GasLimit, DeployInfo.GasPrice), output);
        }

        private Command baseCommand(string nextCommand, ConsoleOutput output)
        {
            if (!output.Ok)
                throw new Exception(string.Format("Error: {0}", output.Output));

            return new Command() { Comm = nextCommand };
        }

        private Command processBigVariableCommand(BigVariableData bigVariableData, Func<ConsoleOutput, Command> currentFunction)
        {
            Command nextCommand = new Command();
            if (string.IsNullOrEmpty(bigVariableData.RemainingString))
            {
                nextCommand.Comm = string.Format("var {0} = {1}", bigVariableData.BaseName, string.Join("+", bigVariableData.VariableNames));
                nextCommand.ReturnFunction = bigVariableData.NextFunction;
            }
            else
            {
                ++bigVariableData.SplitCounter;
                string variableName = string.Format("{0}{1}", bigVariableData.BaseName, bigVariableData.SplitCounter);
                string commandInput = string.Format("var {0} = ", variableName);
                int remainingCharacters = MAX_CHAR_STAND_INPUT_WRITE - commandInput.Length - 2; //2 is for variable quotes 
                //whether string variable is already lesser than maximun
                if (bigVariableData.RemainingString.Length <= (remainingCharacters + 1) && bigVariableData.VariableNames.Count == 0)
                {
                    nextCommand.Comm = string.Format("var {0} = '{1}'", bigVariableData.BaseName, bigVariableData.RemainingString);
                    nextCommand.ReturnFunction = bigVariableData.NextFunction;
                }
                else
                {
                    string variable = bigVariableData.RemainingString.Substring(0, Math.Min(remainingCharacters, bigVariableData.RemainingString.Length));
                    bigVariableData.RemainingString = bigVariableData.RemainingString.Substring(variable.Length);
                    nextCommand.Comm = string.Format("{0}'{1}'", commandInput, variable);
                    nextCommand.ReturnFunction = currentFunction;
                    bigVariableData.VariableNames.Add(variableName);
                }
            }
            return nextCommand;
        }
        #endregion
        private string readOutputStream(StreamReader output, int attempt = 1)
        {
            int current = -1, previous = -1;
            List<char> buffer = new List<char>();
            do
            {
                if (current != -1)
                    buffer.Add((char)current);
                previous = current;
                current = output.Read();
            } while (current != -1 && (current != 32 || previous != 62));
            if (buffer.Count == 0)
                throw new Exception("Failed to read console output. No character buffered.");
       
            string consoleOutput = new string(buffer.ToArray()).TrimEnd('>').Trim('\n', '\r');
            if (string.IsNullOrEmpty(consoleOutput))
            {
                if (attempt > 5)
                    throw new Exception("Failed to read console output. Output cannot be read.");

                Thread.Sleep(200);
                return readOutputStream(output, attempt + 1);
            }
            else
                return consoleOutput.Trim('"', ' ');
        }
        
        private void validateAccountUnlocked(ConsoleOutput output)
        {
            bool unlocked = false;
            if (!(output.Ok && bool.TryParse(output.Output, out unlocked) && unlocked))
                throw new Exception(string.Format("Failed to unlock account. Return: {0}", output.Output));
        }

        private List<Event> parseEventResult(string eventResult, List<CompleteVariableType> eventParameters)
        {
            if (string.IsNullOrEmpty(eventResult))
                return null;
            else
            {
                EventData[] events = JsonConvert.DeserializeObject<EventData[]>(eventResult);
                if (events == null || events.Length == 0)
                    return null;
                else
                {
                    if ((eventParameters == null && events[0].Topics.Length > 1) ||
                        (eventParameters != null && eventParameters.Where(c => c.Indexed).Count() != (events[0].Topics.Length - 1)))
                        throw new Exception("Wrong information for event indexed parameters.");

                    int chunkData = 0;
                    if (!string.IsNullOrEmpty(events[0].Data) && events[0].Data.Length >= 66)
                        chunkData = events[0].Data.Substring(2).Length / 64;
                    
                    if ((eventParameters == null && chunkData > 0) ||
                        (eventParameters != null && eventParameters.Where(c => !c.Indexed).Count() != chunkData))
                        throw new Exception("Wrong information for event not indexed parameters.");

                    List<Event> parsedEvents = new List<Event>();
                    foreach (EventData eventData in events)
                    {
                        Event e = new Event();
                        e.BlockNumber = eventData.BlockNumber;
                        e.TransactionHash = eventData.TransactionHash;
                        e.Data = new List<Variable>();
                        if (eventParameters != null)
                        {
                            string[] datas = null;
                            if (!string.IsNullOrEmpty(eventData.Data) && eventData.Data.Length >= 66)
                            {
                                datas = Enumerable.Range(0, eventData.Data.Substring(2).Length / 64)
                                            .Select(c => eventData.Data.Substring(2).Substring(c * 64, 64)).ToArray();
                            }
                            int indexed = 0, normal = 0;
                            foreach (CompleteVariableType type in eventParameters)
                            {
                                if (type.Indexed)
                                {
                                    e.Data.Add(parseStringToVariable(eventData.Topics[indexed + 1].Substring(2), type.Type, type.Decimals, true));
                                    ++indexed;
                                }
                                else
                                {
                                    e.Data.Add(parseStringToVariable(datas[normal], type.Type, type.Decimals, true));
                                    ++normal;
                                }
                            }
                        }
                        parsedEvents.Add(e);
                    }
                    return parsedEvents;
                }
            }
        }

        private Variable parseStringToVariable(string data, VariableType type, int? decimals = null, bool isEvent = false)
        {
            Variable param = new Variable();
            param.Type = type;
            switch (type)
            {
                case VariableType.Address:
                    if (isEvent)
                        param.Value = string.Format("0x{0}", data.Substring(24));
                    else if (data.StartsWith("0x"))
                        param.Value = data;
                    else
                        param.Value = string.Format("0x{0}", data);
                    break;
                case VariableType.BigNumber:
                    data = data.TrimStart('0');
                    if (string.IsNullOrEmpty(data))
                        param.Value = 0;
                    else
                    {
                        string bigNumber;
                        if (isEvent)
                            bigNumber = BigInteger.Parse(string.Format("0{0}", data), NumberStyles.HexNumber).ToString();
                        else
                            bigNumber = data;
                        param.Value = new BigNumber(GetBigNumberStringToDouble(bigNumber, decimals.Value), decimals.Value);
                    }
                    break;
                case VariableType.Number:
                    data = data.TrimStart('0');
                    if (string.IsNullOrEmpty(data))
                        param.Value = 0;
                    else
                    {
                        if (isEvent)
                            param.Value = int.Parse(data, NumberStyles.HexNumber);
                        else
                            param.Value = int.Parse(data);
                    }
                    break;
                case VariableType.Bool:
                    param.Value = data[data.Length - 1] == '1' || data == "true";
                    break;
                case VariableType.Text:
                    param.Value = data;
                    break;
                default:
                    throw new Exception("Invalid parameter type.");
            }
            return param;
        }

        private static void ValidateGasValues(int gasLimit, int gweiGasPrice)
        {
            if (gasLimit < 21000)
                throw new ArgumentException("Invalid gas limit.", "gasLimit");
            if (gweiGasPrice < 15)
                throw new ArgumentException("Invalid gas price.", "gweiGasPrice");
        }

        private static void ValidateAddress(string address)
        {
            if (string.IsNullOrEmpty(address) || !IS_ADDRESS.IsMatch(address))
                throw new ArgumentException("Invalid adresss.");
        }

        private static void ValidateContractData(string scAddress, string abi, string eventFunctionName)
        {
            ValidateAddress(scAddress);
            if (string.IsNullOrEmpty(abi))
                throw new ArgumentNullException("Invalid smart contract ABI.", "abi");
            if (string.IsNullOrEmpty(eventFunctionName))
                throw new ArgumentNullException("Invalid smart contract function/event.");
        }
                
        private Command getUnlockAccountCommand(string address, string password, Func<ConsoleOutput, Command> returnFunction, int timeUnlocked = 180)
        {
            if (string.IsNullOrEmpty(address))
                throw new ArgumentNullException("Address must be filled.", "address");

            if (string.IsNullOrEmpty(password))
                throw new ArgumentNullException("Password must be filled.", "password");

            return new Command()
            {
                Comm = string.Format("personal.unlockAccount(\"{0}\", \"{1}\", {2})", address, password, timeUnlocked),
                ReturnFunction = returnFunction
            };
        }

        private static string GetBigNumberFormatted(BigNumber bigNumber, bool isEvent = false)
        {
            if (bigNumber == null)
                throw new ArgumentNullException("Big Number cannot be null.");
            if (bigNumber.Value < 0)
                throw new ArgumentException("Big Number cannot be negative.");
            if (bigNumber.Decimals < 0)
                throw new ArgumentException("Decimals cannot be negative.");
            
            string[] numberFractions = bigNumber.Value.ToString(DOUBLE_FIXED_POINT).Replace(',', '.').Split('.');
            string decimals = "";
            string nonDecimal = numberFractions[0];
            if (numberFractions.Length == 1)
            {
                if (nonDecimal != "0")
                    decimals = "".PadLeft(bigNumber.Decimals, '0');
            }
            else 
            {
                if (nonDecimal == "0")
                    nonDecimal = "";
                if (numberFractions[1].Length > bigNumber.Decimals)
                    decimals = numberFractions[1].Substring(0, bigNumber.Decimals);
                else
                    decimals = numberFractions[1].PadRight(bigNumber.Decimals, '0').TrimStart('0');
            }
            string numberParsed = BigInteger.Parse(string.Format("{0}{1}", nonDecimal, decimals)).ToString("X");
            return string.Format("\"0x{0}{1}\"", isEvent ? "".PadLeft(64 - numberParsed.Length, '0') : "", numberParsed);
        }

        private static string GetParameterListFormatted(params Variable[] parameters)
        {
            if (parameters == null)
                return "";
            return string.Join(",", parameters.Select(c => GetParameterFormatted(c)));
        }

        private static string GetParameterFormatted(Variable param)
        {
            if (param == null)
                throw new ArgumentException("Parameter cannot be null.");

            string formattedParameter = "";
            switch(param.Type)
            {
                case VariableType.Address:
                    if (!(param.Value is string))
                        throw new ArgumentException("Invalid address type.");
                    ValidateAddress((string)param.Value);
                    if (param is EventParameter)
                        formattedParameter = string.Format("0x{0}{1}", "".PadLeft(24, '0'), ((string)param.Value).Substring(2));
                    else
                        formattedParameter = (string)param.Value;
                    formattedParameter = string.Format("\"{0}\"", formattedParameter);
                    break;
                case VariableType.BigNumber:
                    if (!(param.Value is BigNumber))
                        throw new ArgumentException("Invalid Big Number.");
                    formattedParameter = GetBigNumberFormatted((BigNumber)param.Value, param is EventParameter);
                    break;
                case VariableType.Number:
                    if (!(param.Value is int))
                        throw new ArgumentException("Invalid Number.");
                    if (param is EventParameter)
                        formattedParameter = GetBigNumberFormatted(new BigNumber(((int)param.Value), 0), true);
                    else
                        formattedParameter = ((int)param.Value).ToString();
                    break;
                case VariableType.Bool:
                    if (!(param.Value is bool))
                        throw new ArgumentException("Invalid Boolean.");
                    if (param is EventParameter)
                        formattedParameter = string.Format("\"0x{0:D63}{1}\"", 0, ((bool)param.Value) ? 1 : 0);
                    else
                        formattedParameter = ((bool)param.Value).ToString().ToLower();
                    break;
                case VariableType.Null:
                    formattedParameter = "null";
                    break;
                default:
                    if (!(param.Value is string) || string.IsNullOrEmpty((string)param.Value))
                        throw new ArgumentException("Invalid Text type.");
                    formattedParameter = string.Format("\"{0}\"", param.Value);
                    break;
            }
            return formattedParameter;
        }
        
        private static string GetTransactionCompleted(string transactionHash, int amountAttempts, int waitingTime)
        {
            Web3 web3 = new Web3();
            string output = null;
            int attempt = 0;
            while (attempt < amountAttempts && output == null)
            {
                if (attempt != 0)
                    Thread.Sleep(waitingTime);

                attempt++;
                output = web3.getTransaction(transactionHash);
            }
            return output;
        }

        private static KeyValuePair<string, string> GetSourceAddres(KeyValuePair<string, string> sourceAddressPassword)
        {
            if (default(KeyValuePair<string, string>).Equals(sourceAddressPassword))
                return MAIN_ADDRESS;
            else
            {
                ValidateAddress(sourceAddressPassword.Key);
                if (string.IsNullOrEmpty(sourceAddressPassword.Value))
                    throw new ArgumentException("Invalid password.");

                return sourceAddressPassword;
            }
        }
        #endregion
        #region Entities
        internal class Wallet
        {
            public string Address { get; set; }
            public string FileName { get; set; }
            public byte[] File { get; set; }
        }

        internal class Transaction
        {
            [JsonProperty(PropertyName = "contractAddress")]
            public string ContractAddress { get; set; }
            [JsonProperty(PropertyName = "blockNumber")]
            public string BlockNumber { get; set; }
            [JsonProperty(PropertyName = "transactionHash")]
            public string TransactionHash { get; set; }
            [JsonProperty(PropertyName = "from")]
            public string From { get; set; }
            [JsonProperty(PropertyName = "gasUsed")]
            public int GasUsed { get; set; }
        }

        internal class Variable
        {
            public VariableType Type { get; set; }
            public object Value { get; set; }

            public Variable() { }

            public Variable(VariableType type, object value)
            {
                Type = type;
                Value = value;
            }
        }

        internal class Event
        {
            public int BlockNumber { get; set; }
            public string TransactionHash { get; set; }
            public List<Variable> Data { get; set; }
        }

        internal class EventParameter : Variable
        {
            public EventParameter() : base() { }

            public EventParameter(VariableType type, object value) : base(type, value) { }
        }

        internal class CompleteVariableType
        {
            public VariableType Type { get; set; }
            public bool Indexed { get; set; }
            public int Decimals { get; set; }

            public CompleteVariableType() { }

            public CompleteVariableType(VariableType type, int decimals = ETHEREUM_DECIMAL, bool indexed = false)
            {
                Type = type;
                Decimals = decimals;
                Indexed = indexed;
            }

            public string getTypeDescription()
            {
                switch(Type)
                {
                    case VariableType.Address:
                        return "address";
                    case VariableType.BigNumber:
                        return "uint256";
                    case VariableType.Number:
                        return "uint64";
                    case VariableType.Bool:
                        return "bool";
                    case VariableType.Text:
                        return "string";
                    default:
                        throw new Exception("Invalid parameter type.");
                }
            }
        }
        
        internal class BigNumber
        {
            public double Value { get; set; }
            public int Decimals { get; set; }

            public BigNumber() { }

            public BigNumber(double value, int decimals)
            {
                Decimals = decimals;
                Value = value;
            }
        }
        
        private class DeployData
        {
            public string ABI { get; set; }
            public string Binary { get; set; }
            public int GasLimit { get; set; }
            public string GasPrice { get; set; }
            public string Name { get; set; }
            public string Address { get; set; }
        }

        private class TransactionData
        {
            public string From { get; set; }
            public string To { get; set; }
            public int GasLimit { get; set; }
            public string GasPrice { get; set; }
            public string Value { get; set; }
            public string Function { get; set; }
            public string Parameters { get; set; }
        }

        private class BigVariableData
        {
            public string RemainingString { get; set; }
            public string BaseName { get; set; }
            public int SplitCounter { get; set; }
            public List<string> VariableNames { get; set; }
            public Func<ConsoleOutput, Command> NextFunction { get; set; }
        }

        private class EventData
        {
            [JsonProperty(PropertyName = "blockNumber")]
            public int BlockNumber { get; set; }
            [JsonProperty(PropertyName = "transactionHash")]
            public string TransactionHash { get; set; }
            [JsonProperty(PropertyName = "data")]
            public string Data { get; set; }
            [JsonProperty(PropertyName = "topics")]
            public string[] Topics { get; set; }
        }
        #endregion
    }
}
