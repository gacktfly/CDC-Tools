using CdcTools.CdcReader.Changes;
using System;
using System.Collections.Generic;
using System.Text;

namespace CdcTools.CdcToKafka.Streaming
{
    public class RowChange
    {
        public RowChange()
        {
            Data = new Dictionary<string, object>();
        }

        public string Lsn { get; set; }
        public string SeqVal { get; set; }
        public string ChangeKey { get; set; }
        public ChangeType ChangeType { get; set; }
        public Dictionary<string, object> Data { get; set; }
        public string Database { get; set; }
        public string Schema { get; set; }
        public string Table { get; set; }
    }
}
