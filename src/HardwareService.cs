using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
namespace PersonalKeypad {
    public interface IKeypadTransport : IDisposable {
        HidDeviceInfo Info {get;}
        void Send(byte[] report);
        byte[] Read(int timeoutMilliseconds);
    }
    public sealed class HardwareStatus {
        public bool Prepared;
        public string Message,BackupPath;
    }
    public sealed class KeypadBackup {
        public int Format=1;
        public string CreatedUtc,Serial;
        public ushort Vendor,Product,Version;
        public Dictionary<string,string> Records;
    }
    public static class HardwareService {
        static JavaScriptSerializer Json() {return new JavaScriptSerializer {MaxJsonLength=1024*1024};}
        static HardwareStatus Run(string directory,Func<IKeypadTransport,string,HardwareStatus> action) {
            directory=Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            using(var mutex=new Mutex(false,@"Local\PersonalKeypad-Hardware-514C8851")) {
                bool owned=false;
                try {
                    try {owned=mutex.WaitOne(0);} catch(AbandonedMutexException) {owned=true;}
                    if(!owned)throw new InvalidOperationException("Another keypad operation is running. Wait for it to finish.");
                    using(var transport=new WindowsHid()) {return action(transport,directory);}
                } finally {if(owned)mutex.ReleaseMutex();}
            }
        }
        public static HardwareStatus Inspect(string directory) {return Run(directory,InspectOn);}
        public static HardwareStatus Prepare(string directory) {return Run(directory,PrepareOn);}
        public static HardwareStatus Restore(string directory) {return Run(directory,RestoreOn);}
        static void ValidateDevice(HidDeviceInfo info) {
            if(info==null||info.Vendor!=0x514C||info.Product!=0x8851||info.UsagePage!=0xFF00||
                info.Usage!=1||info.InputLength!=65||info.OutputLength!=65||String.IsNullOrEmpty(info.Serial))
                throw new InvalidOperationException("The keypad identity or report sizes do not match the supported device.");
        }
        static List<byte[]> Query(IKeypadTransport transport,byte[] request) {
            transport.Send(request);
            var result=new List<byte[]>();
            var timer=Stopwatch.StartNew();
            while(timer.ElapsedMilliseconds<2500 && result.Count<100) {
                byte[] reply=transport.Read(result.Count==0?650:180);
                if(reply==null)break;
                result.Add(reply);
            }
            if(result.Count>=100||timer.ElapsedMilliseconds>=2500)
                throw new InvalidOperationException("The keypad response did not end within its expected bounds.");
            return result;
        }
        public static Dictionary<string,byte[]> ReadSnapshot(IKeypadTransport transport,string directory) {
            ValidateDevice(transport.Info);
            var trace=new List<object>();
            var all=new List<byte[]>();
            try {
                var identity=Query(transport,KeypadProtocol.Report(3,0xFB,0xFB,0xFB));
                int status=-1;
                foreach(byte[] r in identity) {
                    trace.Add(new {query="identify",response=Convert.ToBase64String(r),bytes=r.Length});
                    if(r.Length>=2&&r[0]==3&&r[1]==0xFB) {
                        int next=KeypadProtocol.Identify(r);
                        if(status!=-1&&status!=next)throw new InvalidOperationException("Conflicting device identity replies.");
                        status=next;
                    }
                }
                if(status!=0)throw new InvalidOperationException(status<0?
                    "No two-key identity reply was received. Reconnect the keypad and try again.":
                    "This firmware revision has not been validated. No bindings were changed.");
                for(byte layer=1;layer<=3;layer++) {
                    byte[] request=KeypadProtocol.Report(3,0xFA,0x0F,3,layer);
                    foreach(byte[] r in Query(transport,request)) {
                        trace.Add(new {query=BitConverter.ToString(request,0,5),response=Convert.ToBase64String(r),bytes=r.Length});
                        all.Add(r);
                    }
                }
                var records=KeypadProtocol.Records(all);
                // This firmware exposes 24 storage slots per layer, including
                // unused template slots. Read them all; write only physical 1/2.
                if(records.Count!=72)throw new InvalidOperationException("The complete 72-record configuration was not received. No bindings were changed.");
                for(int layer=1;layer<=3;layer++)
                    for(int slot=1;slot<=24;slot++)
                        if(!records.ContainsKey(layer+":"+slot))throw new InvalidOperationException("A configuration record is missing.");
                KeypadProtocol.RequireBackup(records);
                return records;
            } finally {
                if(!String.IsNullOrEmpty(directory)) {
                    Directory.CreateDirectory(directory);
                    string file=Path.Combine(directory,"read-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".json");
                    File.WriteAllText(file,Json().Serialize(trace));
                }
            }
        }
        static Dictionary<string,byte[]> StableSnapshot(IKeypadTransport t,string directory) {
            var first=ReadSnapshot(t,directory);
            var second=ReadSnapshot(t,directory);
            if(!KeypadProtocol.Equal(first,second))throw new InvalidOperationException("Two configuration reads disagreed. No bindings were changed.");
            return second;
        }
        public static HardwareStatus InspectOn(IKeypadTransport t,string directory) {
            var records=StableSnapshot(t,directory);
            bool prepared=KeypadProtocol.IsPrepared(records);
            return new HardwareStatus {Prepared=prepared,BackupPath=LatestBackup(directory,false),
                Message=prepared?"F13 and F14 are saved on layer 1 and verified by readback. Physical button testing is still required.":
                "Two-key keypad identified. Its configuration was read twice consistently. It is ready for preparation; no bindings were changed."};
        }
        static Dictionary<string,byte[]> Copy(Dictionary<string,byte[]> input) {
            var result=new Dictionary<string,byte[]>();
            foreach(var p in input)result[p.Key]=(byte[])p.Value.Clone();
            return result;
        }
        static string SaveBackup(IKeypadTransport t,string directory,Dictionary<string,byte[]> records) {
            ValidateDevice(t.Info);KeypadProtocol.RequireBackup(records);
            var backup=new KeypadBackup {CreatedUtc=DateTime.UtcNow.ToString("o"),Serial=t.Info.Serial,
                Vendor=t.Info.Vendor,Product=t.Info.Product,Version=t.Info.Version,Records=new Dictionary<string,string>()};
            foreach(var p in records)backup.Records.Add(p.Key,Convert.ToBase64String(p.Value));
            string folder=Path.Combine(directory,"backups");Directory.CreateDirectory(folder);
            string name="before-setup-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".json";
            string path=Path.Combine(folder,name);
            using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
            using(var writer=new StreamWriter(file)) {writer.Write(Json().Serialize(backup));writer.Flush();file.Flush(true);}
            // Read the saved bytes back before allowing a device write.
            var saved=LoadBackup(path,t.Info);
            if(!KeypadProtocol.Equal(saved,records))throw new IOException("The saved backup did not verify.");
            File.WriteAllText(Path.Combine(directory,"last-backup.txt"),name);
            return path;
        }
        static string LatestBackup(string directory,bool required) {
            string index=Path.Combine(directory,"last-backup.txt");
            if(!File.Exists(index)) {if(required)throw new InvalidOperationException("No saved backup is available.");return null;}
            string name=File.ReadAllText(index).Trim();
            if(name.Length==0||name!=Path.GetFileName(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)
                throw new InvalidOperationException("The backup reference is invalid.");
            string path=Path.Combine(directory,"backups",name);
            if(!File.Exists(path))throw new InvalidOperationException("The saved backup file is missing.");
            return path;
        }
        static Dictionary<string,byte[]> LoadBackup(string path,HidDeviceInfo info) {
            if(new FileInfo(path).Length>128*1024)throw new InvalidOperationException("The backup is too large.");
            KeypadBackup backup=Json().Deserialize<KeypadBackup>(File.ReadAllText(path));
            if(backup==null||backup.Format!=1||backup.Vendor!=info.Vendor||backup.Product!=info.Product||
                backup.Version!=info.Version||backup.Serial!=info.Serial||backup.Records==null||backup.Records.Count!=72)
                throw new InvalidOperationException("This backup does not match the connected keypad.");
            var replies=new List<byte[]>();
            foreach(var p in backup.Records) {
                byte[] raw=Convert.FromBase64String(p.Value);
                if(raw.Length!=65||raw[0]!=3||raw[1]!=0xFA||p.Key!=raw[3]+":"+raw[2])
                    throw new InvalidOperationException("The backup contains inconsistent records.");
                replies.Add(raw);
            }
            var result=KeypadProtocol.Records(replies);
            if(result.Count!=72)throw new InvalidOperationException("The backup is incomplete.");
            for(int layer=1;layer<=3;layer++)for(int slot=1;slot<=24;slot++)
                if(!result.ContainsKey(layer+":"+slot))throw new InvalidOperationException("The backup is missing a storage record.");
            KeypadProtocol.RequireBackup(result);return result;
        }
        static void WriteOne(IKeypadTransport t,byte[] report) {
            ValidateDevice(t.Info);
            // Only explicit validated slot/layer keyboard reports reach here.
            byte[] readForm=(byte[])report.Clone();readForm[1]=0xFA;
            byte[] checkedReport=KeypadProtocol.RestoreReport(readForm);
            if(!KeypadProtocol.BytesEqual(report,checkedReport))throw new InvalidOperationException("The proposed binding failed validation.");
            t.Send(report);
            t.Send(KeypadProtocol.Report(3,0xAA,0xAA));
            t.Send(KeypadProtocol.Report(3,0xFD,0xFE,0xFF));
            t.Send(KeypadProtocol.Report(3,0xAA,0xAA));
            Thread.Sleep(200);
            // Consume command acknowledgements before the next read query.
            for(int i=0;i<20;i++)if(t.Read(30)==null)break;
        }
        public static HardwareStatus PrepareOn(IKeypadTransport t,string directory) {
            var before=StableSnapshot(t,directory);
            if(KeypadProtocol.IsPrepared(before))
                return new HardwareStatus {Prepared=true,Message="The keypad already has F13/F14. No bindings were written.",BackupPath=LatestBackup(directory,false)};
            string backup=SaveBackup(t,directory,before);
            var expected=Copy(before);
            var attempted=new List<byte>();
            try {
                for(byte slot=1;slot<=2;slot++) {
                    byte[] write=KeypadProtocol.Binding(slot,(byte)(slot==1?0x68:0x69));
                    expected["1:"+slot]=(byte[])write.Clone();expected["1:"+slot][1]=0xFA;
                    if(KeypadProtocol.BytesEqual(before["1:"+slot],expected["1:"+slot]))continue;
                    attempted.Add(slot);
                    WriteOne(t,write);
                }
                var actual=StableSnapshot(t,directory);
                if(!KeypadProtocol.Equal(actual,expected))throw new InvalidOperationException("Readback did not match the intended changes.");
                return new HardwareStatus {Prepared=true,BackupPath=backup,
                    Message="F13/F14 were saved on layer 1 and read back correctly; all other records are unchanged. Test both physical buttons, then unplug and reconnect to check persistence."};
            } catch(Exception failure) {
                bool restored=false;
                try {
                    foreach(byte slot in attempted)WriteOne(t,KeypadProtocol.RestoreReport(before["1:"+slot]));
                    restored=KeypadProtocol.Equal(StableSnapshot(t,directory),before);
                } catch {}
                throw new InvalidOperationException("Preparation failed: "+failure.GetBaseException().Message+
                    (restored?" The original bindings were restored and read back.":" Restoration could not be confirmed. Keep the backup and reconnect before using Restore.")+
                    " Backup: "+backup,failure);
            }
        }
        public static HardwareStatus RestoreOn(IKeypadTransport t,string directory) {
            ValidateDevice(t.Info);
            string path=LatestBackup(directory,true);
            var original=LoadBackup(path,t.Info);
            var current=StableSnapshot(t,directory);
            foreach(var p in original) {
                if(p.Key=="1:1"||p.Key=="1:2")continue;
                if(!KeypadProtocol.BytesEqual(p.Value,current[p.Key]))
                    throw new InvalidOperationException("Other keypad settings changed since this backup. Restore was stopped to preserve them.");
            }
            for(byte slot=1;slot<=2;slot++) {
                string key="1:"+slot;
                if(KeypadProtocol.BytesEqual(original[key],current[key]))continue;
                byte[] prepared=KeypadProtocol.Binding(slot,(byte)(slot==1?0x68:0x69));prepared[1]=0xFA;
                if(!KeypadProtocol.BytesEqual(current[key],prepared))
                    throw new InvalidOperationException("A button was changed outside this setup. Restore was stopped to preserve the newer setting.");
            }
            for(byte slot=1;slot<=2;slot++)
                if(!KeypadProtocol.BytesEqual(original["1:"+slot],current["1:"+slot]))
                    WriteOne(t,KeypadProtocol.RestoreReport(original["1:"+slot]));
            var actual=StableSnapshot(t,directory);
            if(!KeypadProtocol.Equal(original,actual))throw new InvalidOperationException("Restore was sent, but full readback verification failed. Backup: "+path);
            return new HardwareStatus {Prepared=KeypadProtocol.IsPrepared(actual),BackupPath=path,
                Message="The original two button bindings were restored and read back correctly. Other records are unchanged."};
        }
    }
}
