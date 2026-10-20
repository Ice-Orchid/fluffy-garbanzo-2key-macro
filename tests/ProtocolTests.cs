using System;
using System.Collections.Generic;
using PersonalKeypad;
class ProtocolTests {
    static int tests;
    static void Check(bool condition,string name) { if(!condition) throw new Exception(name); tests++; }
    static void Reject(Action action,string name) {
        try {action();} catch(InvalidOperationException) {tests++;return;}
        throw new Exception("Did not reject: "+name);
    }
    static byte[] Record(byte slot,byte layer,byte key) {
        byte[] r=new byte[65];
        r[0]=3;r[1]=0xFA;r[2]=slot;r[3]=layer;r[4]=1;r[10]=1;r[12]=key;
        return r;
    }
    static int Main() {
        try {
            byte[] f13=KeypadProtocol.Binding(1,0x68);
            Check(f13.Length==65 && f13[0]==3 && f13[1]==0xFE && f13[2]==1 &&
                f13[3]==1 && f13[4]==1 && f13[10]==1 && f13[11]==0 && f13[12]==0x68,"F13 wire encoding");
            byte[] f14=KeypadProtocol.Binding(2,0x69);
            Check(f14[2]==2 && f14[12]==0x69,"F14 wire encoding");
            Reject(delegate {KeypadProtocol.Binding(3,0x68);},"outside two slots");
            Reject(delegate {KeypadProtocol.Binding(1,0x04);},"unapproved key binding");
            Check(KeypadProtocol.Identify(new byte[]{3,0xFB,2,0,0x0A})==0x0A,"identity status");
            Reject(delegate {KeypadProtocol.Identify(new byte[]{3,0xFB,3,0,0});},"wrong key count");
            Reject(delegate {KeypadProtocol.Identify(new byte[]{0,0xFB,2,0,0});},"wrong report id");
            var replies=new List<byte[]> {new byte[]{0},Record(2,1,5),Record(1,2,6),Record(1,1,4)};
            Dictionary<string,byte[]> records=KeypadProtocol.Records(replies);
            Check(records["1:2"][12]==5 && records["1:1"][12]==4 && records["2:1"][12]==6,"own slot/layer, not arrival order");
            Reject(delegate {KeypadProtocol.RequireBackup(new Dictionary<string,byte[]>());},"empty backup");
            var missing=new Dictionary<string,byte[]> {{"1:1",Record(1,1,4)}};
            Reject(delegate {KeypadProtocol.RequireBackup(missing);},"missing second binding");
            var unsafeRecord=Record(1,1,4);unsafeRecord[4]=9;
            Reject(delegate {KeypadProtocol.Records(new List<byte[]> {unsafeRecord});},"unknown kind");
            var shortRecord=new byte[]{3,0xFA,1,1,1};
            Reject(delegate {KeypadProtocol.Records(new List<byte[]> {shortRecord});},"truncated record");
            var longMacro=Record(1,1,4);longMacro[10]=19;
            Reject(delegate {KeypadProtocol.Records(new List<byte[]> {longMacro});},"oversize macro");
            Reject(delegate {KeypadProtocol.Records(new List<byte[]> {Record(1,1,4),Record(1,1,5)});},"conflicting duplicate");
            KeypadProtocol.RequireBackup(records);
            byte[] restored=KeypadProtocol.RestoreReport(records["1:1"]);
            Check(restored[1]==0xFE && restored[12]==4 && records["1:1"][1]==0xFA,"restore is copy, preserves original");
            Check(KeypadProtocol.Equal(records,KeypadProtocol.Records(replies)),"repeatable snapshots");
            var changed=KeypadProtocol.Records(new List<byte[]> {Record(1,1,4),Record(2,1,6),Record(1,2,6)});
            Check(!KeypadProtocol.Equal(records,changed),"changed snapshot refused");
            Check(KeypadProtocol.IsPrepared(KeypadProtocol.Records(new List<byte[]> {Record(1,1,0x68),Record(2,1,0x69)})),"prepared readback");
            Check(!KeypadProtocol.IsPrepared(records),"old keys not ready");
            var templates=KeypadProtocol.Records(new List<byte[]> {Record(24,1,38)});
            Check(templates.ContainsKey("1:24"),"hidden template slots preserved for comparison");
            Reject(delegate {KeypadProtocol.RestoreReport(templates["1:24"]);},"never write template slots");
            Console.WriteLine("PASS: "+tests+" protocol checks");
            return 0;
        } catch(Exception e) {Console.Error.WriteLine("FAIL: "+e.Message);return 1;}
    }
}
