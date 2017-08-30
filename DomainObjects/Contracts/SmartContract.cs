﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Auctus.DomainObjects.Contracts
{
    public class SmartContract
    {
        public Int32 Id { get; set; }
        public String Name { get; set; }
        public Int32 GasLimit { get; set; }
        public String ABI { get; set; }
    }
}
