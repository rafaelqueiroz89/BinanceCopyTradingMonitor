using System;
using System.Collections.Generic;

namespace BinanceCopyTradingMonitor
{
    public class PortfolioData
    {
        public decimal InitialValue { get; set; } = 0;
        public DateTime InitialDate { get; set; } = DateTime.Now;
        public decimal CurrentValue { get; set; } = 0;
        public List<GrowthUpdate> GrowthUpdates { get; set; } = new();
        public List<Withdrawal> Withdrawals { get; set; } = new();
    }
    
    public class GrowthUpdate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Date { get; set; } = DateTime.Now;
        public decimal Value { get; set; }
        public string Notes { get; set; } = "";
    }
    
    public class Withdrawal
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Date { get; set; } = DateTime.Now;
        public decimal Amount { get; set; }
        public string Category { get; set; } = ""; // credit_card, fiat_eur, fiat_brl, fiat_other, voucher_uber, voucher_other
        public string Description { get; set; } = "";
        public string Currency { get; set; } = "USDT";
        
        public static class Categories
        {
            public const string CreditCard = "credit_card";
            public const string FiatEur = "fiat_eur";
            public const string FiatBrl = "fiat_brl";
            public const string FiatOther = "fiat_other";
            public const string VoucherUber = "voucher_uber";
            public const string VoucherOther = "voucher_other";
        }
    }
}


