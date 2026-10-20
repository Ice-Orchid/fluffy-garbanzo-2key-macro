using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
namespace PersonalKeypad {
    public sealed class HidDeviceInfo {
        public string Path, Serial;
        public ushort Vendor, Product, Version, Usage, UsagePage, InputLength, OutputLength;
    }
    public sealed class WindowsHid : IKeypadTransport {
        [StructLayout(LayoutKind.Sequential)] struct InterfaceData {
            public int Size; public Guid Class; public int Flags; public IntPtr Reserved;
        }
        [StructLayout(LayoutKind.Sequential)] struct Attributes {
            public int Size; public ushort Vendor,Product,Version;
        }
        [StructLayout(LayoutKind.Sequential)] struct Caps {
            public ushort Usage,UsagePage,InputLength,OutputLength,FeatureLength;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=17)] public ushort[] Reserved;
            public ushort LinkCollections,InputButtons,InputValues,InputIndices;
            public ushort OutputButtons,OutputValues,OutputIndices,FeatureButtons,FeatureValues,FeatureIndices;
        }
        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid guid,string enumerator,IntPtr parent,uint flags);
        [DllImport("setupapi.dll",SetLastError=true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr info,ref Guid guid,uint index,ref InterfaceData data);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set,ref InterfaceData data,IntPtr detail,uint size,out uint needed,IntPtr info);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)]
        static extern bool CancelIoEx(SafeFileHandle handle,IntPtr overlapped);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle handle,ref Attributes attributes);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle,out IntPtr data);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,out Caps caps);
        [DllImport("hid.dll")] static extern bool HidD_GetSerialNumberString(SafeFileHandle handle,IntPtr buffer,uint bytes);
        FileStream input,output;
        SafeFileHandle inputHandle,outputHandle;
        Task<int> pending;
        byte[] pendingBuffer;
        public HidDeviceInfo Info {get;private set;}
        static HidDeviceInfo Describe(SafeFileHandle handle,string path) {
            var a=new Attributes {Size=Marshal.SizeOf(typeof(Attributes))};
            if(!HidD_GetAttributes(handle,ref a))throw new IOException("Cannot identify the keypad.");
            IntPtr data;
            if(!HidD_GetPreparsedData(handle,out data))throw new IOException("Cannot read the HID capabilities.");
            Caps c;
            try {if(HidP_GetCaps(data,out c)!=0x00110000)throw new IOException("Invalid HID capabilities.");}
            finally {HidD_FreePreparsedData(data);}
            string serial=null;
            IntPtr buffer=Marshal.AllocHGlobal(512);
            try {Marshal.WriteInt16(buffer,0);if(HidD_GetSerialNumberString(handle,buffer,512))serial=Marshal.PtrToStringUni(buffer);}
            finally {Marshal.FreeHGlobal(buffer);}
            return new HidDeviceInfo {Path=path,Serial=serial,Vendor=a.Vendor,Product=a.Product,Version=a.Version,
                Usage=c.Usage,UsagePage=c.UsagePage,InputLength=c.InputLength,OutputLength=c.OutputLength};
        }
        static void Validate(HidDeviceInfo d) {
            if(d.Vendor!=0x514C||d.Product!=0x8851||d.UsagePage!=0xFF00||d.Usage!=1||
                d.InputLength!=65||d.OutputLength!=65||d.Path.IndexOf("&mi_00",StringComparison.OrdinalIgnoreCase)<0)
                throw new InvalidOperationException("The connected device is not the supported keypad configuration interface.");
        }
        void ValidateSame(HidDeviceInfo current) {
            Validate(current);
            if(current.Serial!=Info.Serial||current.Version!=Info.Version)
                throw new InvalidOperationException("The keypad identity changed. Close setup and identify it again.");
        }
        public static HidDeviceInfo Find() {
            var devices=new List<HidDeviceInfo>();
            Guid guid;HidD_GetHidGuid(out guid);
            IntPtr set=SetupDiGetClassDevs(ref guid,null,IntPtr.Zero,18);
            if(set==new IntPtr(-1))throw new System.ComponentModel.Win32Exception();
            try {
                for(uint index=0;;index++) {
                    var data=new InterfaceData {Size=Marshal.SizeOf(typeof(InterfaceData))};
                    if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,index,ref data)) {
                        int error=Marshal.GetLastWin32Error();if(error==259)break;
                        throw new System.ComponentModel.Win32Exception(error);
                    }
                    uint needed;
                    SetupDiGetDeviceInterfaceDetail(set,ref data,IntPtr.Zero,0,out needed,IntPtr.Zero);
                    if(needed<8)throw new IOException("Missing HID interface path.");
                    IntPtr detail=Marshal.AllocHGlobal((int)needed);
                    string path;
                    try {
                        Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);
                        if(!SetupDiGetDeviceInterfaceDetail(set,ref data,detail,needed,out needed,IntPtr.Zero))
                            throw new System.ComponentModel.Win32Exception();
                        path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));
                    } finally {Marshal.FreeHGlobal(detail);}
                    if(path.IndexOf("vid_514c&pid_8851&mi_00",StringComparison.OrdinalIgnoreCase)<0)continue;
                    using(var handle=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
                        if(handle.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Cannot open keypad metadata.");
                        HidDeviceInfo d=Describe(handle,path);Validate(d);devices.Add(d);
                    }
                }
            } finally {SetupDiDestroyDeviceInfoList(set);}
            if(devices.Count!=1)throw new InvalidOperationException(devices.Count==0?
                "The two-button keypad is not connected. Plug it in and try again.":
                "More than one matching keypad is connected. Leave only the keypad you want to configure.");
            return devices[0];
        }
        public WindowsHid() {
            Info=Find();
            try {
                var readHandle=CreateFile(Info.Path,0x80000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
                if(readHandle.IsInvalid) {readHandle.Dispose();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Cannot read the keypad.");}
                try {ValidateSame(Describe(readHandle,Info.Path));inputHandle=readHandle;input=new FileStream(readHandle,FileAccess.Read,65,true);}
                catch {readHandle.Dispose();throw;}
                var writeHandle=CreateFile(Info.Path,0x40000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
                if(writeHandle.IsInvalid) {writeHandle.Dispose();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Cannot query the keypad.");}
                // A one-byte managed buffer makes every 65-byte report a direct
                // write. Buffered Flush/Dispose could otherwise reissue a report
                // after an I/O timeout. Never retrieve FileStream.SafeFileHandle:
                // that getter itself flushes managed buffering.
                try {ValidateSame(Describe(writeHandle,Info.Path));outputHandle=writeHandle;output=new FileStream(writeHandle,FileAccess.Write,1,true);}
                catch {writeHandle.Dispose();throw;}
            } catch {Dispose();throw;}
        }
        public void Send(byte[] report) {
            if(report==null||report.Length!=65||report[0]!=3)throw new InvalidOperationException("Invalid output report.");
            // Re-identify the open handle before each report, rather than trusting cached enumeration.
            ValidateSame(Describe(outputHandle,Info.Path));
            Task write=output.WriteAsync(report,0,report.Length);
            if(!write.Wait(1500)) {
                write.ContinueWith(delegate(Task task) {var observed=task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
                Dispose();throw new TimeoutException("The keypad stopped responding while sending a report.");
            }
        }
        public byte[] Read(int timeoutMilliseconds) {
            if(pending==null) {pendingBuffer=new byte[65];pending=input.ReadAsync(pendingBuffer,0,65);}
            if(!pending.Wait(timeoutMilliseconds))return null;
            int count=pending.Result;
            pending=null;
            if(count==0)throw new IOException("The keypad was disconnected.");
            byte[] result=new byte[count];Array.Copy(pendingBuffer,result,count);return result;
        }
        public void Dispose() {
            if(inputHandle!=null&&!inputHandle.IsClosed)CancelIoEx(inputHandle,IntPtr.Zero);
            if(outputHandle!=null&&!outputHandle.IsClosed)CancelIoEx(outputHandle,IntPtr.Zero);
            if(pending!=null)pending.ContinueWith(delegate(Task<int> task) {var observed=task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            if(input!=null) {input.Dispose();input=null;}
            if(output!=null) {output.Dispose();output=null;}
        }
    }
}
