﻿using Auctus.Business.Funds;
using Auctus.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Text;

namespace Auctus.UnitTest
{
    [TestClass]
    public class SaveCompleteEntryTest
    {
        [TestMethod]
        public void Teste()
        {
            new PensionFundBusiness().CreateCompleteEntry(
                new Fund() { Fee = 5, LatePaymentFee = 5 },
                new Company() { BonusFee = 100, MaxBonusFee = 10 },
                new Employee() { ContributionPercentage = 10, Salary = 2000 },
                new Contract(){
                    VestingRules = new List<VestingRules>() {
                        new VestingRules() {
                            Percentage=20,
                            Period = 1
                        },
                        new VestingRules() {
                            Percentage=40,
                            Period = 2
                        },
                        new VestingRules() {
                            Percentage=60,
                            Period = 3
                        },
                        new VestingRules() {
                            Percentage=80,
                            Period = 4
                        },
                        new VestingRules() {
                            Percentage=100,
                            Period = 5
                        }
                    }
                }
            );
        }
    }
}
