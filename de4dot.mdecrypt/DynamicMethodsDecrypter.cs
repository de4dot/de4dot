/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security;
using dnlib.DotNet;
using dnlib.DotNet.MD;
using dnlib.PE;
using de4dot.blocks;

#if NET35
namespace System.Runtime.ExceptionServices {
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
	class HandleProcessCorruptedStateExceptionsAttribute : Attribute {
	}
}
#endif

namespace de4dot.mdecrypt {
	public class DynamicMethodsDecrypter {
		static DynamicMethodsDecrypter instance;
		DecryptMethodsInfo decryptMethodsInfo;

		struct FuncPtrInfo<D> {
			public D del;
			public IntPtr ptr;
			public IntPtr ptrInDll;

			public void Prepare(Delegate del) {
				RuntimeHelpers.PrepareDelegate(del);
				ptr = Marshal.GetFunctionPointerForDelegate(del);
			}
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		struct IMAGE_SECTION_HEADER {
			public ulong name;
			public uint VirtualSize;
			public uint VirtualAddress;
			public uint SizeOfRawData;
			public uint PointerToRawData;
			public uint PointerToRelocations;
			public uint PointerToLinenumbers;
			public ushort NumberOfRelocations;
			public ushort NumberOfLinenumbers;
			public uint Characteristics;
		}

		[StructLayout(LayoutKind.Sequential, Pack=1, Size=0x88)]
		struct CORINFO_METHOD_INFO {
			public IntPtr ftn;
			public IntPtr scope;
			public IntPtr ILCode;
			public uint ILCodeSize;
			public ushort maxStack;
			public ushort EHCount;
			// 0x64 other bytes here...
		}

		class DecryptContext {
			public DumpedMethod dm;
			public MethodDef method;
		}

		FuncPtrInfo<CompileMethod> ourCompileMethodInfo = new FuncPtrInfo<CompileMethod>();
		FuncPtrInfo<ReturnMethodToken> returnMethodTokenInfo = new FuncPtrInfo<ReturnMethodToken>();
		FuncPtrInfo<ReturnNameOfMethod> returnNameOfMethodInfo = new FuncPtrInfo<ReturnNameOfMethod>();

		IntPtr origCompileMethod;
		//IntPtr jitterTextFreeMem;

		IntPtr callMethod;
		CallMethod callMethodDelegate;

		IntPtr jitterInstance;
		IntPtr jitterVtbl;
		Module moduleToDecrypt;
		IntPtr hInstModule;
		IntPtr ourCompMem;
		bool compileMethodIsThisCall;
		IntPtr ourCodeAddr;

		MDTable methodDefTable;
		IntPtr methodDefTablePtr;
		ModuleDefMD dnlibModule;
		MethodDef moduleCctor;
		uint moduleCctorCodeRva;
		IntPtr moduleToDecryptScope;

		DecryptContext ctx = new DecryptContext();

		public static DynamicMethodsDecrypter Instance {
			get {
				if (instance != null)
					return instance;
				return instance = new DynamicMethodsDecrypter();
			}
		}

		static Version VersionNet45DevPreview = new Version(4, 0, 30319, 17020);
		static Version VersionNet45Rtm = new Version(4, 0, 30319, 17929);
		DynamicMethodsDecrypter() {
			if (UIntPtr.Size != 4)
				throw new ApplicationException("Only 32-bit dynamic methods decryption is supported");

			// .NET 4.5 beta/preview/RC compileMethod has thiscall calling convention, but they
			// switched back to stdcall in .NET 4.5 RTM
			compileMethodIsThisCall = Environment.Version >= VersionNet45DevPreview &&
				Environment.Version < VersionNet45Rtm;
		}

		[DllImport("kernel32", CharSet = CharSet.Ansi)]
		static extern IntPtr GetModuleHandle(string name);

		[DllImport("kernel32", CharSet = CharSet.Ansi)]
		static extern IntPtr GetProcAddress(IntPtr hModule, string name);

		[DllImport("kernel32")]
		static extern bool VirtualProtect(IntPtr addr, int size, uint newProtect, out uint oldProtect);
		const uint PAGE_EXECUTE_READWRITE = 0x40;

		[DllImport("kernel32")]
		static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

		[DllImport("kernel32")]
		static extern bool GetModuleHandleEx(uint dwFlags, IntPtr lpModuleName, out IntPtr phModule);

		delegate IntPtr GetJit();
		delegate int CompileMethod(IntPtr jitter, IntPtr comp, IntPtr info, uint flags, IntPtr nativeEntry, IntPtr nativeSizeOfCode, out bool handled);
		delegate int ReturnMethodToken();
		delegate string ReturnNameOfMethod();
		delegate int CallMethod(IntPtr compileMethod, IntPtr jitter, IntPtr comp, IntPtr info, uint flags, IntPtr nativeEntry, IntPtr nativeSizeOfCode);

		public DecryptMethodsInfo DecryptMethodsInfo {
			set => decryptMethodsInfo = value;
		}

		public unsafe Module Module {
			set {
				if (moduleToDecrypt != null)
					throw new ApplicationException("Module has already been initialized");

				moduleToDecrypt = value;
				hInstModule = Marshal.GetHINSTANCE(moduleToDecrypt);
				moduleToDecryptScope = GetScope(moduleToDecrypt);

				dnlibModule = ModuleDefMD.Load(hInstModule);
				methodDefTable = dnlibModule.TablesStream.MethodTable;
				methodDefTablePtr = new IntPtr((byte*)hInstModule + (uint)dnlibModule.Metadata.PEImage.ToRVA(methodDefTable.StartOffset));

				InitializeDNLibMethods();
			}
		}

		static IntPtr GetScope(Module module) {
			var obj = GetFieldValue(module.ModuleHandle, "m_ptr");
			if (obj is IntPtr)
				return (IntPtr)obj;
			if (obj.GetType().ToString() == "System.Reflection.RuntimeModule")
				return (IntPtr)GetFieldValue(obj, "m_pData");

			throw new ApplicationException($"m_ptr is an invalid type: {obj.GetType()}");
		}

		static object GetFieldValue(object obj, string fieldName) {
			var field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field == null)
				throw new ApplicationException($"Could not get field {obj.GetType()}::{fieldName}");
			return field.GetValue(obj);
		}

		unsafe void InitializeDNLibMethods() {
			moduleCctor = dnlibModule.GlobalType.FindStaticConstructor();
			if (moduleCctor == null)
				moduleCctorCodeRva = 0;
			else {
				byte* p = (byte*)hInstModule + (uint)moduleCctor.RVA;
				if ((*p & 3) == 2)
					moduleCctorCodeRva = (uint)moduleCctor.RVA + 1;
				else
					moduleCctorCodeRva = (uint)((uint)moduleCctor.RVA + (p[1] >> 4) * 4);
			}
		}

		public unsafe void InstallCompileMethod() {
			var hJitterDll = GetJitterDllHandle();
			/*jitterTextFreeMem =*/ GetEndOfText(hJitterDll);

			var getJitPtr = GetProcAddress(hJitterDll, "getJit");
			var getJit = (GetJit)Marshal.GetDelegateForFunctionPointer(getJitPtr, typeof(GetJit));
			jitterInstance = getJit();
			jitterVtbl = *(IntPtr*)jitterInstance;
			origCompileMethod = *(IntPtr*)jitterVtbl;

			PrepareMethods();
			InitializeDelegateFunctionPointers();
			CreateOurCode();
			callMethodDelegate = (CallMethod)Marshal.GetDelegateForFunctionPointer(callMethod, typeof(CallMethod));

			WriteCompileMethod(ourCompileMethodInfo.ptrInDll);
		}

		unsafe void WriteCompileMethod(IntPtr newCompileMethod) {
			if (!VirtualProtect(jitterVtbl, IntPtr.Size, PAGE_EXECUTE_READWRITE, out uint oldProtect))
				throw new ApplicationException("Could not enable write access to jitter vtbl");
			*(IntPtr*)jitterVtbl = newCompileMethod;
			VirtualProtect(jitterVtbl, IntPtr.Size, oldProtect, out oldProtect);
		}

		void InitializeDelegateFunctionPointers() {
			ourCompileMethodInfo.Prepare(ourCompileMethodInfo.del = TheCompileMethod);
			returnMethodTokenInfo.Prepare(returnMethodTokenInfo.del = ReturnMethodToken2);
			returnNameOfMethodInfo.Prepare(returnNameOfMethodInfo.del = ReturnNameOfMethod2);
		}

		public void LoadObfuscator() => RuntimeHelpers.RunModuleConstructor(moduleToDecrypt.ModuleHandle);

		public unsafe bool CanDecryptMethods() =>
			*(IntPtr*)jitterVtbl != ourCompileMethodInfo.ptrInDll &&
			*(IntPtr*)jitterVtbl != origCompileMethod;

		unsafe static IntPtr GetEndOfText(IntPtr hDll) {
			byte* p = (byte*)hDll;
			p += *(uint*)(p + 0x3C);	// add DOSHDR.e_lfanew
			p += 4;
			int numSections = *(ushort*)(p + 2);
			int sizeOptionalHeader = *(ushort*)(p + 0x10);
			p += 0x14;
			//uint sectionAlignment = *(uint*)(p + 0x20);
			p += sizeOptionalHeader;

			var textName = new byte[8] { (byte)'.', (byte)'t', (byte)'e', (byte)'x', (byte)'t', 0, 0, 0 };
			var name = new byte[8];
			var pSection = (IMAGE_SECTION_HEADER*)p;
			for (int i = 0; i < numSections; i++, pSection++) {
				Marshal.Copy(new IntPtr(pSection), name, 0, name.Length);
				if (!CompareName(textName, name, name.Length))
					continue;

				uint size = pSection->VirtualSize;
				uint rva = pSection->VirtualAddress;
				int displ = -8;
				return new IntPtr((byte*)hDll + rva + size + displ);
			}

			throw new ApplicationException("Could not find .text section");
		}

		static bool CompareName(byte[] b1, byte[] b2, int len) {
			for (int i = 0; i < len; i++) {
				if (b1[i] != b2[i])
					return false;
			}
			return true;
		}

		void PrepareMethods() {
			Marshal.PrelinkAll(GetType());
			foreach (var methodInfo in GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
				RuntimeHelpers.PrepareMethod(methodInfo.MethodHandle);
		}

		unsafe void CreateOurCode() {
			var code = new NativeCodeGenerator();

			// our compileMethod() func
			int compileMethodOffset = code.Size;

			int numPushedArgs = compileMethodIsThisCall ? 5 : 6;

			code.WriteByte(0x51);			// push ecx
			code.WriteByte(0x50);			// push eax
			code.WriteByte(0x54);			// push esp
			for (int i = 0; i < 5; i++)
				WritePushDwordPtrEspDispl(code, (sbyte)(0xC + numPushedArgs * 4));	// push dword ptr [esp+XXh]
			if (!compileMethodIsThisCall)
				WritePushDwordPtrEspDispl(code, (sbyte)(0xC + numPushedArgs * 4));	// push dword ptr [esp+XXh]
			else
				code.WriteByte(0x51);		// push ecx
			code.WriteCall(ourCompileMethodInfo.ptr);
			code.WriteByte(0x5A);			// pop edx
			code.WriteByte(0x59);			// pop ecx
			code.WriteBytes(0x84, 0xD2);	// test dl, dl
			code.WriteBytes(0x74, 0x03);	// jz $+5
			code.WriteBytes(0xC2, (ushort)(numPushedArgs * 4)); // retn 14h/18h
			for (int i = 0; i < numPushedArgs; i++)
				WritePushDwordPtrEspDispl(code, (sbyte)(numPushedArgs * 4));	// push dword ptr [esp+XXh]
			code.WriteCall(origCompileMethod);
			code.WriteBytes(0xC2, (ushort)(numPushedArgs * 4)); // retn 14h/18h

			// Our callMethod() code. 1st arg is the method to call. stdcall calling convention.
			int callMethodOffset = code.Size;
			code.WriteByte(0x58);			// pop eax (ret addr)
			code.WriteByte(0x5A);			// pop edx (method to call)
			if (compileMethodIsThisCall)
				code.WriteByte(0x59);		// pop ecx (this ptr)
			code.WriteByte(0x50);			// push eax (ret addr)
			code.WriteBytes(0xFF, 0xE2);	// jmp edx

			// Returns token of method
			int getMethodTokenOffset = code.Size;
			code.WriteCall(returnMethodTokenInfo.ptr);
			code.WriteBytes(0xC2, (ushort)(IntPtr.Size * 2));

			// Returns name of method
			int getMethodNameOffset = code.Size;
			code.WriteCall(returnNameOfMethodInfo.ptr);
			code.WriteBytes(0xC2, (ushort)(IntPtr.Size * 3));

			ourCodeAddr = VirtualAlloc(IntPtr.Zero, new UIntPtr((ulong)code.Size), 0x00001000, PAGE_EXECUTE_READWRITE);
			var baseAddr = ourCodeAddr;
			ourCompileMethodInfo.ptrInDll = new IntPtr((byte*)baseAddr + compileMethodOffset);
			callMethod = new IntPtr((byte*)baseAddr + callMethodOffset);
			returnMethodTokenInfo.ptrInDll = new IntPtr((byte*)baseAddr + getMethodTokenOffset);
			returnNameOfMethodInfo.ptrInDll = new IntPtr((byte*)baseAddr + getMethodNameOffset);
			byte[] theCode = code.GetCode(baseAddr);
			Marshal.Copy(theCode, 0, baseAddr, theCode.Length);
		}

		// Writes push dword ptr [esp+displ]
		static void WritePushDwordPtrEspDispl(NativeCodeGenerator code, sbyte displ) {
			code.WriteBytes(0xFF, 0x74);
			code.WriteBytes(0x24, (byte)displ);
		}

		static IntPtr GetJitterDllHandle() {
			var hJitterDll = GetModuleHandle("mscorjit");
			if (hJitterDll == IntPtr.Zero)
				hJitterDll = GetModuleHandle("clrjit");
			if (hJitterDll == IntPtr.Zero)
				throw new ApplicationException("Could not get a handle to the jitter DLL");
			return hJitterDll;
		}

		unsafe int TheCompileMethod(IntPtr jitter, IntPtr comp, IntPtr info, uint flags, IntPtr nativeEntry, IntPtr nativeSizeOfCode, out bool handled) {
			if (ourCompMem != IntPtr.Zero && comp == ourCompMem) {
				// We're decrypting methods
				var info2 = (CORINFO_METHOD_INFO*)info;
				ctx.dm.code = new byte[info2->ILCodeSize];

				Marshal.Copy(info2->ILCode, ctx.dm.code, 0, ctx.dm.code.Length);
				ctx.dm.mhMaxStack = info2->maxStack;
				ctx.dm.mhCodeSize = info2->ILCodeSize;
				if ((ctx.dm.mhFlags & 8) != 0)
					ctx.dm.extraSections = ReadExtraSections((byte*)info2->ILCode + info2->ILCodeSize);

				UpdateFromMethodDefTableRow();

				handled = true;
				return 0;
			}
			else {
				// We're not decrypting methods

				var info2 = (CORINFO_METHOD_INFO*)info;
				if (info2->scope != moduleToDecryptScope ||
					decryptMethodsInfo.moduleCctorBytes == null ||
					moduleCctorCodeRva == 0) {
					handled = false;
					return 0;
				}

				uint codeRva = (uint)((byte*)info2->ILCode - (byte*)hInstModule);
				if (moduleCctorCodeRva == codeRva) {
					fixed (byte* newIlCodeBytes = &decryptMethodsInfo.moduleCctorBytes[0]) {
						WriteCompileMethod(origCompileMethod);
						info2->ILCode = new IntPtr(newIlCodeBytes);
						info2->ILCodeSize = (uint)decryptMethodsInfo.moduleCctorBytes.Length;
						handled = true;
						return callMethodDelegate(origCompileMethod, jitter, comp, info, flags, nativeEntry, nativeSizeOfCode);
					}
				}
			}

			handled = false;
			return 0;
		}

		unsafe static byte* Align(byte* p, int alignment) =>
			(byte*)new IntPtr((long)((ulong)(p + alignment - 1) & ~(ulong)(alignment - 1)));

		unsafe static byte[] ReadExtraSections(byte* p) {
			p = Align(p, 4);
			byte* startPos = p;
			p = ParseSection(p);
			int size = (int)(p - startPos);
			var sections = new byte[size];
			Marshal.Copy(new IntPtr(startPos), sections, 0, sections.Length);
			return sections;
		}

		unsafe static byte* ParseSection(byte* p) {
			byte flags;
			do {
				p = Align(p, 4);

				flags = *p++;
				if ((flags & 1) == 0)
					throw new ApplicationException("Not an exception section");
				if ((flags & 0x3E) != 0)
					throw new ApplicationException("Invalid bits set");

				if ((flags & 0x40) != 0) {
					p--;
					int num = (int)(*(uint*)p >> 8) / 24;
					p += 4 + num * 24;
				}
				else {
					int num = *p++ / 12;
					p += 2 + num * 12;
				}
			} while ((flags & 0x80) != 0);
			return p;
		}

		unsafe void UpdateFromMethodDefTableRow() {
			uint methodIndex = ctx.dm.token - 0x06000001;
			byte* row = (byte*)methodDefTablePtr + methodIndex * methodDefTable.RowSize;
			ctx.dm.mdRVA = Read(row, methodDefTable.Columns[0]);
			ctx.dm.mdImplFlags = (ushort)Read(row, methodDefTable.Columns[1]);
			ctx.dm.mdFlags = (ushort)Read(row, methodDefTable.Columns[2]);
			ctx.dm.mdName = Read(row, methodDefTable.Columns[3]);
			ctx.dm.mdSignature = Read(row, methodDefTable.Columns[4]);
			ctx.dm.mdParamList = Read(row, methodDefTable.Columns[5]);
		}

		static unsafe uint Read(byte* row, ColumnInfo colInfo) {
			switch (colInfo.Size) {
			case 1: return *(row + colInfo.Offset);
			case 2: return *(ushort*)(row + colInfo.Offset);
			case 4: return *(uint*)(row + colInfo.Offset);
			default: throw new ApplicationException($"Unknown size: {colInfo.Size}");
			}
		}

		string ReturnNameOfMethod2() => ctx.method.Name.String;
		int ReturnMethodToken2() => ctx.method.MDToken.ToInt32();

		public DumpedMethods DecryptMethods() {
			if (!CanDecryptMethods())
				throw new ApplicationException("Can't decrypt methods since compileMethod() isn't hooked yet");
			InstallCompileMethod2();

			var dumpedMethods = new DumpedMethods();

			if (decryptMethodsInfo.methodsToDecrypt == null) {
				for (uint rid = 1; rid <= methodDefTable.Rows; rid++)
					dumpedMethods.Add(DecryptMethod(0x06000000 + rid));
			}
			else {
				foreach (var token in decryptMethodsInfo.methodsToDecrypt)
					dumpedMethods.Add(DecryptMethod(token));
			}

			return dumpedMethods;
		}

		unsafe DumpedMethod DecryptMethod(uint token) {
			if (!CanDecryptMethods())
				throw new ApplicationException("Can't decrypt methods since compileMethod() isn't hooked yet");

			ctx = new DecryptContext();
			ctx.dm = new DumpedMethod();
			ctx.dm.token = token;

			ctx.method = dnlibModule.ResolveMethod(MDToken.ToRID(token));
			if (ctx.method == null)
				throw new ApplicationException($"Could not find method {token:X8}");

			byte* mh = (byte*)hInstModule + (uint)ctx.method.RVA;
			byte* code;
			if (mh == (byte*)hInstModule) {
				ctx.dm.mhMaxStack = 0;
				ctx.dm.mhCodeSize = 0;
				ctx.dm.mhFlags = 0;
				ctx.dm.mhLocalVarSigTok = 0;
				code = null;
			}
			else if ((*mh & 3) == 2) {
				uint headerSize = 1;
				ctx.dm.mhMaxStack = 8;
				ctx.dm.mhCodeSize = (uint)(*mh >> 2);
				ctx.dm.mhFlags = 2;
				ctx.dm.mhLocalVarSigTok = 0;
				code = mh + headerSize;
			}
			else {
				uint headerSize = (uint)((mh[1] >> 4) * 4);
				ctx.dm.mhMaxStack = *(ushort*)(mh + 2);
				ctx.dm.mhCodeSize = *(uint*)(mh + 4);
				ctx.dm.mhFlags = *(ushort*)mh;
				ctx.dm.mhLocalVarSigTok = *(uint*)(mh + 8);
				code = mh + headerSize;
			}

			CORINFO_METHOD_INFO info = default;
			info.ILCode = new IntPtr(code);
			info.ILCodeSize = ctx.dm.mhCodeSize;
			info.maxStack = ctx.dm.mhMaxStack;
			info.scope = moduleToDecryptScope;

			InitializeOurComp();
			if (code == null) {
				ctx.dm.code = new byte[0];
				UpdateFromMethodDefTableRow();
			}
			else
				callMethodDelegate(*(IntPtr*)jitterVtbl, jitterInstance, ourCompMem, new IntPtr(&info), 0, new IntPtr(0x12345678), new IntPtr(0x3ABCDEF0));

			var dm = ctx.dm;
			ctx = null;
			return dm;
		}

		unsafe void InitializeOurComp() {
			const int numIndexes = 15;
			if (ourCompMem == IntPtr.Zero)
				ourCompMem = Marshal.AllocHGlobal(numIndexes * IntPtr.Size);
			if (ourCompMem == IntPtr.Zero)
				throw new ApplicationException("Could not allocate memory");

			var mem = (IntPtr*)ourCompMem;
			for (int i = 0; i < numIndexes; i++)
				mem[i] = IntPtr.Zero;

			mem[1] = new IntPtr(mem + 2);
			mem[3] = new IntPtr(IntPtr.Size * 5);
			mem[5] = new IntPtr(IntPtr.Size * 7);
			mem[6] = new IntPtr(mem + 7);
			mem[7] = returnNameOfMethodInfo.ptrInDll;
			mem[8] = new IntPtr(mem);
			mem[13] = returnMethodTokenInfo.ptrInDll;	// .NET 2.0
			mem[14] = returnMethodTokenInfo.ptrInDll;	// .NET 4.0
		}

		bool hasInstalledCompileMethod2 = false;
		unsafe void InstallCompileMethod2() {
			if (hasInstalledCompileMethod2)
				return;

			if (!PatchCM(*(IntPtr*)jitterVtbl, origCompileMethod, ourCompileMethodInfo.ptrInDll))
				throw new ApplicationException("Couldn't patch compileMethod");

			hasInstalledCompileMethod2 = true;
			return;
		}

		static IntPtr GetModuleHandle(IntPtr addr) {
			if (!GetModuleHandleEx(4, addr, out var hModule))
				throw new ApplicationException("GetModuleHandleEx() failed");
			return hModule;
		}

		class PatchInfo {
			public int RVA;
			public byte[] Data;
			public byte[] Orig;

			public PatchInfo(int rva, byte[] data, byte[] orig) {
				RVA = rva;
				Data = data;
				Orig = orig;
			}
		}
		static readonly PatchInfo[] patches = new PatchInfo[] {
			new PatchInfo(0x000110A5, new byte[] { 0x33, 0xC0, 0xC2, 0x04, 0x00 }, new byte[] { 0xE9, 0x36, 0x3A, 0x00, 0x00 }),
			new PatchInfo(0x000110AF, new byte[] { 0x33, 0xC0, 0xC2, 0x04, 0x00 }, new byte[] { 0xE9, 0x4C, 0x3C, 0x00, 0x00 }),
			new PatchInfo(0x000110AA, new byte[] { 0x33, 0xC0, 0xC2, 0x04, 0x00 }, new byte[] { 0xE9, 0xF1, 0x3A, 0x00, 0x00 }),
			new PatchInfo(0x00011019, new byte[] { 0x33, 0xC0, 0xC2, 0x04, 0x00 }, new byte[] { 0xE9, 0x12, 0x4B, 0x00, 0x00 }),
			new PatchInfo(0x00011019, new byte[] { 0x33, 0xC0, 0xC2, 0x04, 0x00 }, new byte[] { 0xE9, 0x02, 0x4B, 0x00, 0x00 }),
			new PatchInfo(0x00011019, new byte[] { 0x33, 0xC0, 0xC2, 0x04, 0x00 }, new byte[] { 0xE9, 0xA2, 0x4B, 0x00, 0x00 }),
		};

		static unsafe bool PatchCM(IntPtr addr, IntPtr origValue, IntPtr newValue) {
			var baseAddr = GetModuleHandle(addr);
			IntPtr patchAddr;
			using (var peImage = new PEImage(baseAddr))
				patchAddr = FindCMAddress(peImage, baseAddr, origValue);
			if (patchAddr == IntPtr.Zero)
				return false;

			*(IntPtr*)patchAddr = newValue;
			PatchRT(baseAddr);
			return true;
		}

		[HandleProcessCorruptedStateExceptions, SecurityCritical]	// Req'd on .NET 4.0
		static unsafe bool PatchRT(IntPtr baseAddr) {
			foreach (var info in patches) {
				try {
					var addr = new IntPtr(baseAddr.ToInt64() + info.RVA);

					var data = new byte[info.Orig.Length];
					Marshal.Copy(addr, data, 0, data.Length);
					if (!Equals(data, info.Orig))
						continue;

					if (!VirtualProtect(addr, info.Data.Length, PAGE_EXECUTE_READWRITE, out uint oldProtect))
						throw new ApplicationException("Could not enable write access");
					Marshal.Copy(info.Data, 0, addr, info.Data.Length);
					VirtualProtect(addr, info.Data.Length, oldProtect, out oldProtect);
					return true;
				}
				catch {
				}
			}
			return false;
		}

		static bool Equals(byte[] a, byte[] b) {
			if (a == b)
				return true;
			if (a == null || b == null)
				return false;
			if (a.Length != b.Length)
				return false;
			for (int i = 0; i < a.Length; i++) {
				if (a[i] != b[i])
					return false;
			}
			return true;
		}

		[HandleProcessCorruptedStateExceptions, SecurityCritical]	// Req'd on .NET 4.0
		static unsafe IntPtr FindCMAddress(PEImage peImage, IntPtr baseAddr, IntPtr origValue) {
			int offset = Environment.Version.Major == 2 ? 0x10 : 0x28;

			foreach (var section in peImage.ImageSectionHeaders) {
				const uint RW = 0x80000000 | 0x40000000;
				if ((section.Characteristics & RW) != RW)
					continue;

				byte* p = (byte*)baseAddr + (uint)section.VirtualAddress + ((section.VirtualSize + IntPtr.Size - 1) & ~(IntPtr.Size - 1)) - IntPtr.Size;
				for (; p >= (byte*)baseAddr; p -= IntPtr.Size) {
					try {
						byte* p2 = (byte*)*(IntPtr**)p;
						if ((ulong)p2 >= 0x10000) {
							if (*(IntPtr*)(p2 + 0x74) == origValue)
								return new IntPtr(p2 + 0x74);
							if (*(IntPtr*)(p2 + 0x78) == origValue)
								return new IntPtr(p2 + 0x78);
						}
					}
					catch {
					}
					try {
						byte* p2 = (byte*)*(IntPtr**)p;
						if ((ulong)p2 >= 0x10000) {
							p2 += offset;
							if (*(IntPtr*)p2 == origValue)
								return new IntPtr(p2);
						}
					}
					catch {
					}
					try {
						if (*(IntPtr*)p == origValue)
							return new IntPtr(p);
					}
					catch {
					}
				}
			}
			return IntPtr.Zero;
		}
	}
}
