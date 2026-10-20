using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using PersonalKeypad;
class HardwareServiceTests {
    sealed class Fake : IKeypadTransport {
        public HidDeviceInfo Info {get;private set;}
        public Dictionary<string,byte[]> Records=new Dictionary<string,byte[]>();
        public List<byte[]> Writes=new List<byte[]>();
        Queue<byte[]> incoming=new Queue<byte[]>();
        byte[] pending;
        public bool Missing,WrongIdentity,Unstable,FailSecond,failed;
        int identifies;
        public Fake() {
            Info=new HidDeviceInfo {Vendor=0x514C,Product=0x8851,Usage=1,UsagePage=0xFF00,InputLength=65,OutputLength=65,Serial="unit-test",Version=256};
            for(byte layer=1;layer<=3;layer++) for(byte slot=1;slot<=24;slot++)
                Records[layer+":"+slot]=KeypadProtocol.Report(3,0xFA,slot,layer,1,0,0,0,0,0,1,0,(byte)(slot+3));
        }
        public void Send(byte[] r) {
            if(r[1]==0xFB) {identifies++;incoming.Enqueue(KeypadProtocol.Report(3,0xFB,(byte)(WrongIdentity?3:2),0,0));return;}
            if(r[1]==0xFA) {
                byte layer=r[4];
                for(byte slot=24;slot>=1;slot--) {
                    if(Missing&&layer==1&&slot==2)continue;
                    byte[] value=(byte[])Records[layer+":"+slot].Clone();
                    if(Unstable&&identifies%2==0&&layer==1&&slot==2)value[12]=99;
                    incoming.Enqueue(value);
                }
                return;
            }
            Writes.Add((byte[])r.Clone());
            if(r[1]==0xFE) {
                if(FailSecond&&!failed&&r[2]==2) {failed=true;throw new IOException("simulated disconnect during second write");}
                pending=(byte[])r.Clone();pending[1]=0xFA;
            }
            if(r[1]==0xFD&&pending!=null) {Records[pending[3]+":"+pending[2]]=pending;pending=null;}
        }
        public byte[] Read(int timeout) {return incoming.Count>0?incoming.Dequeue():null;}
        public void Dispose() {}
    }
    static int checks;
    static void Check(bool c,string label) {if(!c)throw new Exception(label);checks++;}
    static void Throws(Action a,string label) {
        try {a();}catch(InvalidOperationException) {checks++;return;}
        throw new Exception("Did not reject "+label);
    }
    static int Main() {
        string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"hardware-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var missing=new Fake {Missing=true};
            Throws(delegate {HardwareService.PrepareOn(missing,Path.Combine(root,"missing"));},"missing record");
            Check(missing.Writes.Count==0,"incomplete backup has zero device writes");
            var wrong=new Fake {WrongIdentity=true};
            Throws(delegate {HardwareService.PrepareOn(wrong,Path.Combine(root,"wrong"));},"wrong identity");
            Check(wrong.Writes.Count==0,"wrong identity has zero device writes");
            var unstable=new Fake {Unstable=true};
            Throws(delegate {HardwareService.PrepareOn(unstable,Path.Combine(root,"unstable"));},"unstable snapshot");
            Check(unstable.Writes.Count==0,"unstable backup has zero device writes");
            var normal=new Fake();string dir=Path.Combine(root,"normal");
            var prepared=HardwareService.PrepareOn(normal,dir);
            Check(prepared.Prepared&&File.Exists(prepared.BackupPath),"verified saved backup");
            Check(normal.Writes.Count==8,"exactly two binding/finish/commit/finish sequences");
            Check(normal.Records["1:1"][12]==0x68&&normal.Records["1:2"][12]==0x69,"prepared bindings");
            Check(normal.Records["2:1"][12]==4&&normal.Records["1:24"][12]==27,"other layer and template unchanged");
            var again=HardwareService.PrepareOn(normal,dir);
            Check(again.Prepared&&normal.Writes.Count==8,"idempotent prepare");
            HardwareService.RestoreOn(normal,dir);
            Check(normal.Records["1:1"][12]==4&&normal.Records["1:2"][12]==5,"original A/B restored");
            normal.Records["1:24"][12]=88;
            int prior=normal.Writes.Count;
            Throws(delegate {HardwareService.RestoreOn(normal,dir);},"unrelated config change");
            Check(normal.Writes.Count==prior,"restore preserves unrelated later changes");
            var corrupted=new Fake();string corruptDir=Path.Combine(root,"corrupt");
            var corruptReady=HardwareService.PrepareOn(corrupted,corruptDir);
            var serializer=new JavaScriptSerializer();
            var damaged=serializer.Deserialize<KeypadBackup>(File.ReadAllText(corruptReady.BackupPath));
            byte[] changedRecord=Convert.FromBase64String(damaged.Records["1:24"]);
            changedRecord[1]=0xFB;
            damaged.Records["1:24"]=Convert.ToBase64String(changedRecord);
            File.WriteAllText(corruptReady.BackupPath,serializer.Serialize(damaged));
            prior=corrupted.Writes.Count;
            Throws(delegate {HardwareService.RestoreOn(corrupted,corruptDir);},"damaged unused backup record");
            Check(corrupted.Writes.Count==prior,"damaged backup causes zero restoration writes");
            var failure=new Fake {FailSecond=true};
            string error=null;
            try {HardwareService.PrepareOn(failure,Path.Combine(root,"failure"));}
            catch(InvalidOperationException e) {error=e.Message;}
            Check(error!=null&&error.Contains("restored and read back"),"failed second binding reports verified recovery");
            Check(failure.Records["1:1"][12]==4&&failure.Records["1:2"][12]==5,"partial operation rolls back");
            Console.WriteLine("PASS: "+checks+" hardware transaction checks");return 0;
        } catch(Exception e) {Console.Error.WriteLine("FAIL: "+e);return 1;}
        finally {
            string expected=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)+Path.DirectorySeparatorChar;
            string target=Path.GetFullPath(root);
            if(target.StartsWith(expected.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                Directory.Delete(target,true);
        }
    }
}
