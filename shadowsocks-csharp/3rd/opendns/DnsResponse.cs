/* 
 * Author: Ruy Delgado <ruydelgado@gmail.com>
 * Title: OpenDNS
 * Description: DNS Client Library 
 * Revision: 1.0
 * Last Modified: 2005.01.28
 * Created On: 2005.01.28
 * 
 * Note: Based on DnsLite by Jaimon Mathew
 * */

using System;
using System.Net;
using System.Text;
using System.Collections;
using System.Diagnostics;

namespace OpenDNS
{
    /// <summary>
    /// Response object as result of a dns query message. 
    /// Will be null unless query succesfull. 
    /// </summary>
    public class DnsResponse
    {
        private readonly int _QueryID;

        //Property Internals
        private readonly bool _AuthorativeAnswer;
        private readonly bool _IsTruncated;
        private readonly bool _RecursionDesired;
        private readonly bool _RecursionAvailable;
        private readonly ResponseCodes _ResponseCode;

        private readonly ResourceRecordCollection _ResourceRecords;
        private readonly ResourceRecordCollection _Answers;
        private readonly ResourceRecordCollection _Authorities;
        private readonly ResourceRecordCollection _AdditionalRecords;

        //Read Only Public Properties
        public int QueryID
        {
            get => _QueryID;
        }

        public bool AuthorativeAnswer
        {
            get => _AuthorativeAnswer;
        }

        public bool IsTruncated
        {
            get => _IsTruncated;
        }

        public bool RecursionRequested
        {
            get => _RecursionDesired;
        }

        public bool RecursionAvailable
        {
            get => _RecursionAvailable;
        }

        public ResponseCodes ResponseCode
        {
            get => _ResponseCode;
        }

        public ResourceRecordCollection Answers
        {
            get => _Answers;
        }

        public ResourceRecordCollection Authorities
        {
            get => _Authorities;
        }

        public ResourceRecordCollection AdditionalRecords
        {
            get => _AdditionalRecords;
        }

        /// <summary>
        /// Unified collection of Resource Records from Answers, 
        /// Authorities and Additional. NOT IN REALTIME SYNC. 
        /// 
        /// </summary>
        public ResourceRecordCollection ResourceRecords
        {
            get
            {
                if (_ResourceRecords.Count == 0 && _Answers.Count > 0 && _Authorities.Count > 0 && _AdditionalRecords.Count > 0)
                {
                    foreach (ResourceRecord rr in Answers)
                        _ResourceRecords.Add(rr);

                    foreach (ResourceRecord rr in Authorities)
                        _ResourceRecords.Add(rr);

                    foreach (ResourceRecord rr in AdditionalRecords)
                        _ResourceRecords.Add(rr);
                }

                return _ResourceRecords;
            }
        }

        public DnsResponse(int ID, bool AA, bool TC, bool RD, bool RA, int RC)
        {
            _QueryID = ID;
            _AuthorativeAnswer = AA;
            _IsTruncated = TC;
            _RecursionDesired = RD;
            _RecursionAvailable = RA;
            _ResponseCode = (ResponseCodes)RC;

            _ResourceRecords = new ResourceRecordCollection();
            _Answers = new ResourceRecordCollection();
            _Authorities = new ResourceRecordCollection();
            _AdditionalRecords = new ResourceRecordCollection();
        }
    }
}
