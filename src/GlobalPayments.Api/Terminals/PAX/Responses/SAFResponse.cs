using System;
using System.IO;
using System.Collections.Generic;
using GlobalPayments.Api.Terminals.Abstractions;
using GlobalPayments.Api.Terminals.Extensions;

namespace GlobalPayments.Api.Terminals.PAX {
    public class SAFResponse : PaxTerminalResponse, ISAFResponse {
        private HostResponse hostResponse;

        public int TotalCount { get; private set; }
        public decimal? TotalAmount { get; private set; }

        public Dictionary<SummaryType, SummaryResponse> Approved { get; private set; }
        public Dictionary<SummaryType, SummaryResponse> Pending { get; private set; }
        public Dictionary<SummaryType, SummaryResponse> Declined { get; private set; }


        public SAFResponse(byte[] buffer)
            : base(buffer, PAX_MSG_ID.B09_RSP_SAF_UPLOAD) {
        }

        protected override void ParseResponse(BinaryReader br) {
            base.ParseResponse(br);

            this.hostResponse = new HostResponse(br);
        }
    }
}
