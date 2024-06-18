using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScrawlDataIntraday.Models
{
    public class StockMessage
    {
        public string StockCode { get; set; }

        public StockMessage(string stockCode)
        {
            StockCode = stockCode;
        }
    }
}
