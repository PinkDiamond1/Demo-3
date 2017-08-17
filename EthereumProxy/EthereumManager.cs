﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using static Auctus.EthereumProxy.Solc;
using static Auctus.EthereumProxy.Web3;

namespace Auctus.EthereumProxy
{
    public class EthereumManager
    {
        public static void Initialize(string mainAddress, string password)
        {
            Web3.InitializeMainAddress(mainAddress, password);
        }

        #region Methods for Web Api simple Test
        private static string smartContractABI;
        public static WalletInfo CreateAccount(string password)
        {
            try
            {
                Wallet wallet = Web3.CreateAccount(password);
                return new WalletInfo()
                {
                    Address = wallet.Address,
                    FileName = wallet.FileName,
                    File = wallet.File
                };
            }
            catch (Exception e)
            {
                return new WalletInfo() { Address = e.ToString() };
            }
        }

        public static string DeployContratc(string owner, string password, int gasLimit, int gwei)
        {
            try
            {
                SCCompiled compiled = Solc.Compile("Test", smartContractStringified).Single(c => c.Name == "Test");
                smartContractABI = compiled.ABI;
                return Web3.DeployContract(compiled, gasLimit, gwei, new KeyValuePair<string, string>(owner, password));
            }
            catch (Exception e)
            {
                return e.ToString();
            }
        }

        public static double GetBalance(string address)
        {
            return Web3.GetBalance(address);
        }
        
        public static string Send(string from, string password, string to, double value, int gasLimit, int gwei)
        {
            try
            {
                return Web3.Send(to, Web3.ETHER(value), gasLimit, gwei, new KeyValuePair<string, string>(from, password));
            }
            catch (Exception e)
            {
                return e.ToString();
            }
        }

        public static double GetBalanceFromSmartContract(string from, string smartContractAddress)
        {
            return ((BigNumber)Web3.CallConstFunction(new CompleteVariableType(VariableType.BigNumber, 0), smartContractAddress, smartContractABI, "balanceOf", new Variable(VariableType.Address, from)).Value).Value;
        }

        public static string DrainFromContract(string owner, string password, int gasLimit, int gwei, string smartContractAddress)
        {
            try
            {
                return Web3.CallFunction(smartContractAddress, smartContractABI, "drain", Web3.ETHER(0), gasLimit, gwei, new KeyValuePair<string, string>(owner, password));
            }
            catch (Exception e)
            {
                return e.ToString();
            }
        }

        public static TransactionInfo GetTransactionInformation(string transactionHash)
        {
            try
            { 
                Transaction trans = Web3.GetTransaction(transactionHash, 5);
                if (trans != null)
                {
                    return new TransactionInfo()
                    {
                        BlockNumber = trans.BlockNumber,
                        ContractAddress = trans.ContractAddress,
                        From = trans.From,
                        GasUsed = trans.GasUsed,
                        TransactionHash = trans.TransactionHash
                    };
                }
                else
                    return null;
            }
            catch (Exception e)
            {
                return new TransactionInfo() { ContractAddress = e.ToString() };
            }
        }

        [Serializable]
        public class TransactionInfo
        {
            public string ContractAddress { get; set; }
            public string BlockNumber { get; set; }
            public string TransactionHash { get; set; }
            public string From { get; set; }
            public int GasUsed { get; set; }
        }

        [Serializable]
        public class WalletInfo
        {
            public string Address { get; set; }
            public string FileName { get; set; }
            public byte[] File { get; set; }
        }
        #endregion

        #region Test 

        #region SC source
        private const string smartContractStringified = @"
        pragma solidity ^0.4.13;


        library SafeMath {
	        function times(uint256 x, uint256 y) internal returns (uint256) {
		        uint256 z = x * y;
		        assert(x == 0 || (z / x == y));
		        return z;
	        }
	
	        function divided(uint256 x, uint256 y) internal returns (uint256) {
		        assert(y != 0);
		        return x / y;
	        }
	
	        function plus(uint256 x, uint256 y) internal returns (uint256) {
		        uint256 z = x + y;
		        assert(z >= x && z >= y);
		        return z;
	        }
	
	        function minus(uint256 x, uint256 y) internal returns (uint256) {
		        assert(x >= y);
		        return x - y;
	        }
        }


        contract Test {
	        using SafeMath for uint256;
	
	        uint256 public tokensPerEther = 10;
	        uint256 public finishedCap = 200 ether;
	        address public owner;
	
	        mapping(address => uint256) public balances;
	        mapping(address => uint256) public invested; 
	
	        mapping(address => bool) public happy; 
	
	        uint256 private weiRaised = 0;
	        uint256 private distributedAmount = 0;
	
	        bool private halted = false;
	
	        event Buy(address indexed recipient, uint256 amount);
	        event Transfer(address indexed from, address indexed to, uint256 _value, uint64 time);
	        event Happy(bool indexed yes);
	        event Ping();
    
	        modifier onlyOwner() {
		        require(msg.sender == owner);
		        _;
	        }
	
	        modifier buyPeriod() {
		        require(weiRaised < finishedCap);
		        _;
	        }
	
	        modifier isNotHalted() {
		        require(!halted);
		        _;
	        }
	
	        modifier validPayload(uint256 size) { 
		        require(msg.data.length >= (size + 4));
		        _;
	        }
	
	        function Test() {
		        owner = msg.sender;
	        }
	
	        function etherRaised() constant returns (uint256) {
		        return weiRaised;
	        }
	
	        function tokenDistributed() constant returns (uint256) {
		        return distributedAmount;
	        }
	
	        function isHalted() constant returns (bool) {
		        return halted;
	        }
	
	        function balanceOf(address who) constant returns (uint256) {
		        return balances[who];
	        }
	
	        function investementOf(address who) constant returns (uint256) {
		        return invested[who];
	        }
	
	        function isHappy(address who) constant returns (bool) {
		        return happy[who];
	        }
	
	        function()
		        payable 
		        buyPeriod 
		        isNotHalted
	        {		
		        require(msg.value > 0); 
		        uint256 tokenAmount = msg.value.times(tokensPerEther).divided(1 ether);
		        balances[msg.sender] = balances[msg.sender].plus(tokenAmount);
		        invested[msg.sender] = invested[msg.sender].plus(msg.value);
		        distributedAmount = distributedAmount.plus(tokenAmount);
		        weiRaised = weiRaised.plus(msg.value);
		
		        Buy(msg.sender, tokenAmount);
	        }
	
	        function transfer(address to, uint256 value) returns (bool success) { 
     	        balances[msg.sender] = balances[msg.sender].minus(value);
     	        balances[to] = balances[to].plus(value);
     	        Transfer(msg.sender, to, value, uint64(now));
     	        return true;
            }
    
            function IAmHappy(bool yes)
            {
                happy[msg.sender] = yes;
                Happy(yes);
            }
    
            function ping()
            {
                Ping();
            }
		
	        function drain() onlyOwner 
	        {
		        require(msg.sender.send(this.balance));
	        }
	
	        function setHalted(bool halt) onlyOwner {
		        halted = halt;
	        }
        }";
        #endregion

        public static void Test()
        { 
            #region Initialization
            int gwei = 21;
            string password = "test";
            string mainAddress = "0xfa4ef7de49b1460d4114a8385af5cd638cf55b43";
            string secondAddress = "0xc795b00c8a9e5a0413f48424183dae789d7555d5";
            string thirdAddress = "0x3a56c7e6a97842fdc343de56c960db7c33e33c22";
            KeyValuePair<string, string> mainAccount = new KeyValuePair<string, string>(mainAddress, password);
            KeyValuePair<string, string> secondAccount = new KeyValuePair<string, string>(secondAddress, password);
            KeyValuePair<string, string> thirdAccount = new KeyValuePair<string, string>(thirdAddress, password);
            Wallet account = Web3.CreateAccount(password);
            if (account == null)
                throw new Exception();
            string createdAddress = account.Address;
            KeyValuePair<string, string> createdAccount = new KeyValuePair<string, string>(createdAddress, password);
            Initialize(mainAddress, password);
            #endregion


            #region Send
            ValidateTrasaction(Web3.Send(createdAddress, Web3.ETHER(15), 21000, gwei));
            ValidateTrasaction(Web3.Send(createdAddress, Web3.ETHER(0.000000001), 21000, gwei, secondAccount));
            bool okSendInsuficientFunds = false;
            try
            {
                ValidateTrasaction(Web3.Send(createdAddress, Web3.ETHER(99999999999999999999999999.9), 50000, gwei, thirdAccount));//Will fail (invalid amount) 
            }
            catch
            { okSendInsuficientFunds = true; }
            if (!okSendInsuficientFunds)
                throw new Exception();
            if (Web3.GetBalance(createdAddress) != 15.000000001)
                throw new Exception();
            #endregion


            #region Contract Creation
            SCCompiled scCompiled = Solc.Compile("Test", smartContractStringified).Single(c => c.Name == "Test");
            Transaction contractTransaction = Web3.GetTransaction(Web3.DeployContract(scCompiled, 5100000, gwei));
            if (contractTransaction == null)
                throw new Exception();
            string scAddress = contractTransaction.ContractAddress;
            string scABI = scCompiled.ABI;
            #endregion


            #region Buy
            ValidateTrasaction(Web3.Send(scAddress, Web3.ETHER(80), 210000, gwei));
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "setHalted", Web3.ETHER(0), 120000, gwei, mainAccount, new Variable(VariableType.Bool, true)));
            ValidateTrasaction(Web3.Send(scAddress, Web3.ETHER(20), 210000, gwei));//Will Fail (halted)
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "setHalted", Web3.ETHER(0), 120000, gwei, secondAccount, new Variable(VariableType.Bool, false)));
            ValidateTrasaction(Web3.Send(scAddress, Web3.ETHER(30), 210000, gwei, secondAccount));//Will Fail (halted)
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "setHalted", Web3.ETHER(0), 120000, gwei, mainAccount, new Variable(VariableType.Bool, false)));
            ValidateTrasaction(Web3.Send(scAddress, Web3.ETHER(65.5), 210000, gwei, secondAccount)); 
            ValidateTrasaction(Web3.Send(scAddress, Web3.ETHER(45.5), 210000, gwei, thirdAccount));
            ValidateTrasaction(Web3.Send(scAddress, Web3.ETHER(5), 210000, gwei, createdAccount));
            ValidateTrasaction(Web3.Send(scAddress, Web3.ETHER(4), 210000, gwei, createdAccount));
            ValidateTrasaction(Web3.Send(scAddress, Web3.ETHER(15), 210000, gwei, thirdAccount));//Will Fail (buy period end)

            ValidateBalanceInvestiment(scAddress, scABI, mainAddress, 800, 80);
            ValidateBalanceInvestiment(scAddress, scABI, secondAddress, 655, 65.5);
            ValidateBalanceInvestiment(scAddress, scABI, thirdAddress, 455, 45.5);
            ValidateBalanceInvestiment(scAddress, scABI, createdAddress, 90, 9);

            List<CompleteVariableType> eventBuy = new List<CompleteVariableType>();
            eventBuy.Add(new CompleteVariableType(VariableType.Address, 0, true));
            eventBuy.Add(new CompleteVariableType(VariableType.BigNumber, 0));
            List<Event> allBuyEvents = Web3.ReadEvent(scAddress, "Buy", eventBuy);
            if (allBuyEvents.Count != 5 && allBuyEvents.Where(c => c.Data.Any(l => l.Type == VariableType.Address && (string)l.Value == createdAddress)).Count() != 2 &&
                allBuyEvents.Where(c => c.Data.Any(l => l.Type == VariableType.Address && (string)l.Value == createdAddress)).
                    SelectMany(c => c.Data.Where(l => l.Type == VariableType.BigNumber).Select(k => (double)k.Value)).Sum() != 90 &&
                allBuyEvents.Where(c => c.Data.Any(l => l.Type == VariableType.Address && (string)l.Value == mainAddress)).
                    SelectMany(c => c.Data.Where(l => l.Type == VariableType.BigNumber).Select(k => (double)k.Value)).Sum() != 800 &&
                allBuyEvents.Where(c => c.Data.Any(l => l.Type == VariableType.Address && (string)l.Value == secondAddress)).
                    SelectMany(c => c.Data.Where(l => l.Type == VariableType.BigNumber).Select(k => (double)k.Value)).Sum() != 655 &&
                allBuyEvents.Where(c => c.Data.Any(l => l.Type == VariableType.Address && (string)l.Value == thirdAddress)).
                    SelectMany(c => c.Data.Where(l => l.Type == VariableType.BigNumber).Select(k => (double)k.Value)).Sum() != 455)
                throw new Exception();
            List<Event> createdAccountBuyEvents = Web3.ReadEvent(scAddress, "Buy", eventBuy, new EventParameter(VariableType.Address, createdAddress));
            if (createdAccountBuyEvents.Count != 2 && createdAccountBuyEvents.SelectMany(c => c.Data.Where(l => l.Type == VariableType.BigNumber).Select(k => (double)k.Value)).Sum() != 90)
                throw new Exception();
            #endregion


            #region Transfer
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "transfer", Web3.ETHER(0), 200000, gwei, mainAccount, new Variable(VariableType.Address, secondAddress), new Variable(VariableType.BigNumber, new BigNumber(100, 0))));
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "transfer", Web3.ETHER(0), 200000, gwei, thirdAccount, new Variable(VariableType.Address, secondAddress), new Variable(VariableType.BigNumber, new BigNumber(400, 0))));
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "transfer", Web3.ETHER(0), 200000, gwei, mainAccount, new Variable(VariableType.Address, createdAddress), new Variable(VariableType.BigNumber, new BigNumber(150, 0))));
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "transfer", Web3.ETHER(0), 200000, gwei, secondAccount, new Variable(VariableType.Address, createdAddress), new Variable(VariableType.BigNumber, new BigNumber(50, 0))));
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "transfer", Web3.ETHER(0), 200000, gwei, thirdAccount, new Variable(VariableType.Address, secondAddress), new Variable(VariableType.BigNumber, new BigNumber(56, 0)))); //Will Fail (invalid amout)

            ValidateBalance(scAddress, scABI, mainAddress, 550);
            ValidateBalance(scAddress, scABI, secondAddress, 1105);
            ValidateBalance(scAddress, scABI, thirdAddress, 55);
            ValidateBalance(scAddress, scABI, createdAddress, 290);

            List<CompleteVariableType> eventTransfer = new List<CompleteVariableType>();
            eventTransfer.Add(new CompleteVariableType(VariableType.Address, 0, true));
            eventTransfer.Add(new CompleteVariableType(VariableType.Address, 0, true));
            eventTransfer.Add(new CompleteVariableType(VariableType.BigNumber, 0));
            eventTransfer.Add(new CompleteVariableType(VariableType.Number));
            List<Event> allTransferEvents = Web3.ReadEvent(scAddress, "Transfer", eventTransfer);
            if (allTransferEvents.Count != 4 && allTransferEvents.Where(c => c.Data.Any(l => l.Type == VariableType.Address && (string)l.Value == mainAddress)).Count() != 2 &&
                allTransferEvents.Where(c => c.Data.Any(l => l.Type == VariableType.Address && (string)l.Value == secondAddress)).Count() != 3 &&
                allTransferEvents.Where(c => c.Data.Any(l => l.Type == VariableType.Address && (string)l.Value == createdAddress)).Count() != 2 &&
                allTransferEvents.Where(c => c.Data.Any(l => l.Type == VariableType.Address && (string)l.Value == thirdAddress)).Count() != 1 &&
                allTransferEvents.SelectMany(c => c.Data.Where(l => l.Type == VariableType.BigNumber).Select(k => (double)k.Value)).Sum() != 700 &&
                allTransferEvents.Where(c => c.Data.Any(l => l.Type == VariableType.Address && (string)l.Value == thirdAddress)).
                    SelectMany(c => c.Data.Where(l => l.Type == VariableType.Number).Select(k => (double)k.Value)).Sum() <
                allTransferEvents.Where(c => c.Data.Any(l => l.Type == VariableType.Address && (string)l.Value == createdAddress)).
                    SelectMany(c => c.Data.Where(l => l.Type == VariableType.Number).Select(k => (double)k.Value)).Take(1).Sum())
                throw new Exception();
            List<Event> thirdAccountTransferEvents = Web3.ReadEvent(scAddress, "Transfer", eventTransfer, new EventParameter(VariableType.Address, thirdAddress));
            if (thirdAccountTransferEvents.Count != 1 && thirdAccountTransferEvents.Any(c => c.Data.Any(l => l.Type == VariableType.Address && (string)l.Value == createdAddress))
                && thirdAccountTransferEvents.SelectMany(c => c.Data.Where(l => l.Type == VariableType.BigNumber).Select(k => (double)k.Value)).Sum() != 400)
                throw new Exception();
            List<Event> secondAccountToTransferEvents = Web3.ReadEvent(scAddress, "Transfer", eventTransfer, new EventParameter(VariableType.Null, null), new EventParameter(VariableType.Address, secondAddress));
            if (secondAccountToTransferEvents.Count != 2 && secondAccountToTransferEvents.Any(c => c.Data.Any(l => l.Type == VariableType.Address && (string)l.Value == mainAddress))
                && secondAccountToTransferEvents.Any(c => c.Data.Any(l => l.Type == VariableType.Address && (string)l.Value == thirdAddress))
                && secondAccountToTransferEvents.SelectMany(c => c.Data.Where(l => l.Type == VariableType.BigNumber).Select(k => (double)k.Value)).Sum() != 500)
                throw new Exception();
            #endregion


            #region Happy
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "IAmHappy", Web3.ETHER(0), 200000, gwei, mainAccount, new Variable(VariableType.Bool, true)));
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "IAmHappy", Web3.ETHER(0), 200000, gwei, secondAccount, new Variable(VariableType.Bool, true)));
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "IAmHappy", Web3.ETHER(0), 200000, gwei, mainAccount, new Variable(VariableType.Bool, false)));

            if ((bool)Web3.CallConstFunction(new CompleteVariableType(VariableType.Bool), scAddress, scABI, "isHappy", new Variable(VariableType.Address, mainAddress)).Value)
                throw new Exception();
            if (!(bool)Web3.CallConstFunction(new CompleteVariableType(VariableType.Bool), scAddress, scABI, "isHappy", new Variable(VariableType.Address, secondAddress)).Value)
                throw new Exception();
            if ((bool)Web3.CallConstFunction(new CompleteVariableType(VariableType.Bool), scAddress, scABI, "isHappy", new Variable(VariableType.Address, createdAddress)).Value)
                throw new Exception();

            List<CompleteVariableType> eventHappy = new List<CompleteVariableType>();
            eventHappy.Add(new CompleteVariableType(VariableType.Bool, 0, true));
            List<Event> allHappyEvents = Web3.ReadEvent(scAddress, "Happy", eventHappy);
            if (allHappyEvents.Count != 3 || allHappyEvents.Where(c => c.Data.Any(l => (bool)l.Value)).Count() != 2)
                throw new Exception();
            List<Event> unhappyEvents = Web3.ReadEvent(scAddress, "Happy", eventHappy, new EventParameter(VariableType.Bool, true));
            if (unhappyEvents.Count != 2)
                throw new Exception();
            #endregion


            #region Ping
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "ping", Web3.ETHER(0), 30000, gwei));
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "ping", Web3.ETHER(0), 30000, gwei, secondAccount));
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "ping", Web3.ETHER(0), 30000, gwei, createdAccount));

            if (Web3.ReadEvent(scAddress, "Ping", null).Count != 3)
                throw new Exception();
            #endregion


            #region Balance
            double createdAccountBalance = Web3.GetBalance(createdAddress);
            if (createdAccountBalance > 6 || createdAccountBalance < 5.9)
                throw new Exception();

            double mainAccountBalanceBeforeDrain = Web3.GetBalance(mainAddress);
            ValidateTrasaction(Web3.CallFunction(scAddress, scABI, "drain", Web3.ETHER(0), 40000, gwei));
            double mainAccountBalanceAfterDrain = Web3.GetBalance(mainAddress);
            if ((mainAccountBalanceBeforeDrain + 200) <= mainAccountBalanceAfterDrain || (mainAccountBalanceBeforeDrain + 200) > (mainAccountBalanceAfterDrain + 0.01))
                throw new Exception();
            #endregion
        }
        
        private static void ValidateTrasaction(string trasactionHash)
        {
            Transaction transactionToSend = Web3.GetTransaction(trasactionHash, 25);
            if (transactionToSend == null)
                throw new Exception();
        }

        private static void ValidateBalance(string scAddress, string scABI, string address, double balance)
        {
            double token = ((BigNumber)Web3.CallConstFunction(new CompleteVariableType(VariableType.BigNumber, 0), scAddress, scABI, "balanceOf", new Variable(VariableType.Address, address)).Value).Value;
            if (token != balance)
                throw new Exception();
        }

        private static void ValidateBalanceInvestiment(string scAddress, string scABI, string address, double balance, double invested)
        {
            ValidateBalance(scAddress, scABI, address, balance);
            double ether = ((BigNumber)Web3.CallConstFunction(Web3.EtherType, scAddress, scABI, "investementOf", new Variable(VariableType.Address, address)).Value).Value;
            if (ether != invested)
                throw new Exception();
        }
        #endregion
    }
}
