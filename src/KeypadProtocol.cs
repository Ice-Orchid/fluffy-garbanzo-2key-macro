using System;
using System.Collections.Generic;
namespace PersonalKeypad {
    // Independently expressed from the published k884x packet layout.
    // Native Windows reports include the report ID and are exactly 65 bytes.
    public static class KeypadProtocol {
        public static byte[] Report(params byte[] prefix) {
            if(prefix.Length>65) throw new InvalidOperationException("Report is too long.");
            byte[] r=new byte[65];Array.Copy(prefix,r,prefix.Length);return r;
        }
        public static byte[] Binding(byte slot,byte usage) {
            if(slot<1||slot>2||usage!=(slot==1?0x68:0x69))
                throw new InvalidOperationException("Only Button 1 F13 and Button 2 F14 can be programmed.");
            return Report(3,0xFE,slot,1,1,0,0,0,0,0,1,0,usage);
        }
        public static int Identify(byte[] r) {
            if(r==null||r.Length<5||r[0]!=3||r[1]!=0xFB||r[2]!=2)
                throw new InvalidOperationException("The device did not identify as the supported two-key keypad.");
            if(r[4]!=0 && r[4]!=0x0A)
                throw new InvalidOperationException("This keypad firmware variant is not understood.");
            return r[4];
        }
        static byte[] ValidateRecord(byte[] r) {
            if(r==null||r.Length<11||r.Length>65||r[0]!=3||r[1]!=0xFA||
                r[2]<1||r[2]>24||r[3]<1||r[3]>3||r[4]!=1||r[10]>18)
                throw new InvalidOperationException("An unsupported or incomplete key record was returned; no bindings will be written.");
            int used=11+2*r[10];
            if(r.Length<used) throw new InvalidOperationException("Truncated key sequence.");
            for(int i=5;i<10;i++) if(r[i]!=0)
                throw new InvalidOperationException("This record contains unsupported timing or options.");
            for(int i=used;i<r.Length;i++) if(r[i]!=0)
                throw new InvalidOperationException("This record contains unrecognized extra data.");
            return Report(r);
        }
        public static Dictionary<string,byte[]> Records(IEnumerable<byte[]> replies) {
            var records=new Dictionary<string,byte[]>();
            foreach(byte[] r in replies) {
                if(r==null||r.Length<2||r[0]!=3||r[1]!=0xFA) continue;
                byte[] normalized=ValidateRecord(r);
                string key=r[3]+":"+r[2];
                byte[] existing;
                if(records.TryGetValue(key,out existing) && !BytesEqual(existing,normalized))
                    throw new InvalidOperationException("Conflicting configuration records; backup is ambiguous.");
                records[key]=normalized;
            }
            return records;
        }
        public static void RequireBackup(Dictionary<string,byte[]> records) {
            if(records==null||!records.ContainsKey("1:1")||!records.ContainsKey("1:2"))
                throw new InvalidOperationException("Both original button bindings must be read before programming.");
            ValidateRecord(records["1:1"]);ValidateRecord(records["1:2"]);
        }
        public static byte[] RestoreReport(byte[] record) {
            byte[] r=ValidateRecord(record);
            if(r[3]!=1||r[2]>2) throw new InvalidOperationException("Only the two physical buttons on layer 1 can be restored.");
            r[1]=0xFE;return r;
        }
        public static bool BytesEqual(byte[] a,byte[] b) {
            if(a==null||b==null||a.Length!=b.Length)return false;
            for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;
            return true;
        }
        public static bool Equal(Dictionary<string,byte[]> a,Dictionary<string,byte[]> b) {
            if(a.Count!=b.Count)return false;
            foreach(var pair in a) {byte[] value;if(!b.TryGetValue(pair.Key,out value)||!BytesEqual(pair.Value,value))return false;}
            return true;
        }
        public static bool IsPrepared(Dictionary<string,byte[]> records) {
            try {RequireBackup(records);} catch(InvalidOperationException) {return false;}
            for(byte slot=1;slot<=2;slot++) {
                byte[] expected=Binding(slot,(byte)(slot==1?0x68:0x69));expected[1]=0xFA;
                if(!BytesEqual(expected,records["1:"+slot]))return false;
            }
            return true;
        }
    }
}
