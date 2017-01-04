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
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.ILProtector {
	sealed class DynamicMethodsDecrypter : IDisposable {
		ModuleDefMD module;
		Module reflectionModule;
		Module reflectionProtectModule;
		TypeDef protectMainType;
		//Type reflectionProtectMainType;
		FieldInfo invokerFieldInfo;
		ModuleDefMD moduleProtect;
		IDecrypter decrypter;
		bool methodReaderHasDelegateTypeFlag;

		static class CodeAllocator {
			[DllImport("kernel32")]
			static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

			const uint PAGE_EXECUTE_READWRITE = 0x40;
			const uint MEM_COMMIT = 0x00001000;
			const int ALIGNMENT = 0x10;

			static IntPtr currentPage;
			static int nextOffset;
			static int pageSize;

			public static IntPtr Allocate(byte[] code) {
				if (code == null || code.Length == 0)
					return IntPtr.Zero;

				var addr = Allocate(code.Length);
				Marshal.Copy(code, 0, addr, code.Length);
				return addr;
			}

			public static IntPtr Allocate(int size) {
				if (size <= 0)
					return IntPtr.Zero;

				size = (size + ALIGNMENT - 1) & ~(ALIGNMENT - 1);
				if (nextOffset + size > pageSize)
					AllocNewPage(size);

				var data = new IntPtr(currentPage.ToInt64() + nextOffset);
				nextOffset += size;
				return data;
			}

			static void AllocNewPage(int size) {
				size = (size + 0xFFF) & ~0xFFF;
				currentPage = VirtualAlloc(IntPtr.Zero, new UIntPtr((uint)size), MEM_COMMIT, PAGE_EXECUTE_READWRITE);
				if (currentPage == IntPtr.Zero)
					throw new ApplicationException("VirtualAlloc() failed");
				pageSize = size;
				nextOffset = 0;
			}
		}

		interface IDecrypter {
			byte[] Decrypt(int methodId, uint rid);
		}

		abstract class DecrypterBase : IDecrypter {
			protected readonly DynamicMethodsDecrypter dmd;
			protected readonly int appDomainId;
			protected readonly int asmHashCode;

			class PatchData {
				public int RVA { get; set; }
				public byte[] Data { get; set; }

				public PatchData() {
				}

				public PatchData(int rva, byte[] data) {
					this.RVA = rva;
					this.Data = data;
				}
			}

			class PatchInfo {
				public int RvaDecryptMethod { get; set; }
				public List<PatchData> PatchData { get; set; }

				public PatchInfo() {
				}

				public PatchInfo(int rvaDecryptMethod, PatchData patchData) {
					this.RvaDecryptMethod = rvaDecryptMethod;
					this.PatchData = new List<PatchData> { patchData };
				}
			}

			static readonly byte[] nops2 = new byte[] { 0x90, 0x90 };
			static readonly byte[] nops6 = new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
			static readonly Dictionary<Version, PatchInfo> patchInfos32 = new Dictionary<Version, PatchInfo> {
				{ new Version(2, 0, 8, 0),	new PatchInfo(0x00020B20, new PatchData(0x00005733, nops2)) },
				{ new Version(2, 0, 8, 5),	new PatchInfo(0x000221A0, new PatchData(0x00005742, nops2)) },
				{ new Version(2, 0, 9, 0),	new PatchInfo(0x00023360, new PatchData(0x000056F2, nops6)) },
				{ new Version(2, 0, 10, 0), new PatchInfo(0x00023B30, new PatchData(0x00005B12, nops6)) },
				{ new Version(2, 0, 11, 0), new PatchInfo(0x000207C0, new PatchData(0x00018432, nops6)) },
				{ new Version(2, 0, 11, 1), new PatchInfo(0x000207C0, new PatchData(0x00018432, nops6)) },
			};
			static readonly Dictionary<Version, PatchInfo> patchInfos64 = new Dictionary<Version, PatchInfo> {
				{ new Version(2, 0, 8, 0),	new PatchInfo(0x00026090, new PatchData(0x00005E0C, nops6)) },
				{ new Version(2, 0, 8, 5),	new PatchInfo(0x000273D0, new PatchData(0x000060CA, nops6)) },
				{ new Version(2, 0, 9, 0),	new PatchInfo(0x00028B00, new PatchData(0x00005F70, nops6)) },
				{ new Version(2, 0, 10, 0), new PatchInfo(0x00029630, new PatchData(0x00006510, nops6)) },
				{ new Version(2, 0, 11, 0), new PatchInfo(0x000257C0, new PatchData(0x0001C9A0, nops6)) },
				{ new Version(2, 0, 11, 1), new PatchInfo(0x000257C0, new PatchData(0x0001C9A0, nops6)) },
			};

			[DllImport("kernel32")]
			static extern bool VirtualProtect(IntPtr addr, int size, uint newProtect, out uint oldProtect);
			const uint PAGE_EXECUTE_READWRITE = 0x40;

			public DecrypterBase(DynamicMethodsDecrypter dmd) {
				this.dmd = dmd;
				this.appDomainId = AppDomain.CurrentDomain.Id;
				this.asmHashCode = dmd.reflectionModule.Assembly.GetHashCode();
			}

			protected IntPtr GetDelegateAddress(FieldDef delegateField) {
				FieldInfo delegateFieldInfo = dmd.reflectionProtectModule.ResolveField(0x04000000 + (int)delegateField.Rid);
				object mainTypeInst = ((Delegate)dmd.invokerFieldInfo.GetValue(null)).Target;
				return GetNativeAddressOfDelegate((Delegate)delegateFieldInfo.GetValue(mainTypeInst));
			}

			static IntPtr GetNativeAddressOfDelegate(Delegate del) {
				var field = typeof(Delegate).GetField("_methodPtrAux", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field == null)
					return IntPtr.Zero;

				return (IntPtr)field.GetValue(del);
			}

			protected void PatchRuntime(IntPtr decryptAddr) {
				if (!PatchRuntimeInternal(decryptAddr))
					throw new ApplicationException("Probably a new version. Could not patch runtime.");
			}

			bool PatchRuntimeInternal(IntPtr decryptAddr) {
				var patchInfos = IntPtr.Size == 4 ? patchInfos32 : patchInfos64;
				var protectVersion = dmd.reflectionProtectModule.Assembly.GetName().Version;
				PatchInfo info;
				if (!patchInfos.TryGetValue(protectVersion, out info))
					return false;
				return PatchRuntime(decryptAddr, info);
			}

			[HandleProcessCorruptedStateExceptions, SecurityCritical]	// Req'd on .NET 4.0
			static bool PatchRuntime(IntPtr decryptAddr, PatchInfo info) {
				try {
					IntPtr baseAddr = new IntPtr(decryptAddr.ToInt64() - info.RvaDecryptMethod);
					if ((baseAddr.ToInt64() & 0xFFFF) != 0)
						return false;

					if (Marshal.ReadInt16(baseAddr) != 0x5A4D)
						return false;

					foreach (var patchData in info.PatchData) {
						var patchAddr = new IntPtr(baseAddr.ToInt64() + patchData.RVA);
						uint oldProtect;
						if (!VirtualProtect(patchAddr, patchData.Data.Length, PAGE_EXECUTE_READWRITE, out oldProtect))
							return false;
						Marshal.Copy(patchData.Data, 0, patchAddr, patchData.Data.Length);
						VirtualProtect(patchAddr, patchData.Data.Length, oldProtect, out oldProtect);
					}

					return true;
				}
				catch {
				}

				return false;
			}

			public abstract byte[] Decrypt(int methodId, uint rid);
		}

		// 1.0.7.0 - 1.0.8.0
		class DecrypterV1_0_7_0 : DecrypterBase {
			DecryptMethod decryptMethod;

			unsafe delegate bool DecryptMethod(int appDomainId, int asmHashCode, int methodId, out byte* pMethodCode, out int methodSize);

			public DecrypterV1_0_7_0(DynamicMethodsDecrypter dmd, FieldDef delegateField)
				: base(dmd) {
				IntPtr addr = GetDelegateAddress(delegateField);
				decryptMethod = (DecryptMethod)Marshal.GetDelegateForFunctionPointer(addr, typeof(DecryptMethod));
			}

			public unsafe override byte[] Decrypt(int methodId, uint rid) {
				byte* pMethodCode;
				int methodSize;
				if (!decryptMethod(appDomainId, asmHashCode, methodId, out pMethodCode, out methodSize))
					return null;
				byte[] methodData = new byte[methodSize];
				Marshal.Copy(new IntPtr(pMethodCode), methodData, 0, methodData.Length);
				return methodData;
			}
		}

		// 2.0.0.0 - 2.0.7.6
		class DecrypterV2_0_0_0 : DecrypterBase {
			DecryptMethod decryptMethod;

			unsafe delegate bool DecryptMethod(int clrMajorVersion, int appDomainId, int asmHashCode, int methodId, out byte* pMethodCode, out int methodSize);

			public DecrypterV2_0_0_0(DynamicMethodsDecrypter dmd, FieldDef delegateField)
				: base(dmd) {
				IntPtr addr = GetDelegateAddress(delegateField);
				decryptMethod = (DecryptMethod)Marshal.GetDelegateForFunctionPointer(addr, typeof(DecryptMethod));
			}

			public unsafe override byte[] Decrypt(int methodId, uint rid) {
				byte* pMethodCode;
				int methodSize;
				if (!decryptMethod(Environment.Version.Major, appDomainId, asmHashCode, methodId, out pMethodCode, out methodSize))
					return null;
				byte[] methodData = new byte[methodSize];
				Marshal.Copy(new IntPtr(pMethodCode), methodData, 0, methodData.Length);
				return methodData;
			}
		}

		// 2.0.8.0
		class DecrypterV2_0_8_0 : DecrypterBase {
			DecryptMethod decryptMethod;
			byte[] decryptedData;

			delegate bool DecryptMethod(int clrMajorVersion, int appDomainId, int asmHashCode, int methodId, [MarshalAs(UnmanagedType.FunctionPtr)] DecryptCallback decryptCallback, [MarshalAs(UnmanagedType.Interface)] out Delegate createdDelegate);
			unsafe delegate bool DecryptCallback(byte* pMethodCode, int methodSize, [MarshalAs(UnmanagedType.Interface)] ref Delegate createdDelegate);

			public DecrypterV2_0_8_0(DynamicMethodsDecrypter dmd, FieldDef delegateField)
				: base(dmd) {
				IntPtr addr = GetDelegateAddress(delegateField);
				decryptMethod = (DecryptMethod)Marshal.GetDelegateForFunctionPointer(addr, typeof(DecryptMethod));
				PatchRuntime(addr);
			}

			public unsafe override byte[] Decrypt(int methodId, uint rid) {
				Delegate createdDelegate;
				if (!decryptMethod(Environment.Version.Major, appDomainId, asmHashCode, methodId, MyDecryptCallback, out createdDelegate))
					return null;
				return decryptedData;
			}

			unsafe bool MyDecryptCallback(byte* pMethodCode, int methodSize, ref Delegate createdDelegate) {
				decryptedData = new byte[methodSize];
				Marshal.Copy(new IntPtr(pMethodCode), decryptedData, 0, decryptedData.Length);
				return true;
			}
		}

		// 2.0.8.5
		class DecrypterV2_0_8_5 : DecrypterBase {
			DecryptMethod decryptMethod;
			byte[] decryptedData;
			bool decryptReturnValue;

			delegate bool DecryptMethod(int clrMajorVersion, int appDomainId, int asmHashCode, int methodId, [MarshalAs(UnmanagedType.Interface)] StackTrace stackTrace, [MarshalAs(UnmanagedType.FunctionPtr)] DecryptCallback decryptCallback, [MarshalAs(UnmanagedType.Interface)] out Delegate createdDelegate);
			unsafe delegate bool DecryptCallback(byte* pMethodCode, int methodSize, [MarshalAs(UnmanagedType.Interface)] ref Delegate createdDelegate);

			public DecrypterV2_0_8_5(DynamicMethodsDecrypter dmd, FieldDef delegateField)
				: base(dmd) {
				IntPtr addr = GetDelegateAddress(delegateField);
				decryptMethod = (DecryptMethod)Marshal.GetDelegateForFunctionPointer(addr, typeof(DecryptMethod));
				PatchRuntime(addr);
			}

			public unsafe override byte[] Decrypt(int methodId, uint rid) {
				Delegate createdDelegate;
				decryptReturnValue = false;
				if (!decryptMethod(Environment.Version.Major, appDomainId, asmHashCode, methodId, new StackTrace(), MyDecryptCallback, out createdDelegate) &&
					!decryptReturnValue)
					return null;
				return decryptedData;
			}

			unsafe bool MyDecryptCallback(byte* pMethodCode, int methodSize, ref Delegate createdDelegate) {
				createdDelegate = new DecryptCallback(MyDecryptCallback);
				decryptedData = new byte[methodSize];
				Marshal.Copy(new IntPtr(pMethodCode), decryptedData, 0, decryptedData.Length);
				return decryptReturnValue = true;
			}
		}

		// 2.0.9.0 - 2.0.11.1
		class DecrypterV2_0_9_0 : DecrypterBase {
			DecryptMethod decryptMethod;
			byte[] decryptedData;

			delegate bool DecryptMethod(int clrMajorVersion, int appDomainId, int asmHashCode, int methodId, [MarshalAs(UnmanagedType.Interface)] StackTrace stackTrace, [MarshalAs(UnmanagedType.FunctionPtr)] DecryptCallback decryptCallback);
			unsafe delegate bool DecryptCallback(byte* pMethodCode, int methodSize, int methodId);

			public DecrypterV2_0_9_0(DynamicMethodsDecrypter dmd, FieldDef delegateField)
				: base(dmd) {
				IntPtr addr = GetDelegateAddress(delegateField);
				decryptMethod = (DecryptMethod)Marshal.GetDelegateForFunctionPointer(addr, typeof(DecryptMethod));
				PatchRuntime(addr);
			}

			public unsafe override byte[] Decrypt(int methodId, uint rid) {
				var encMethod = this.dmd.reflectionModule.ResolveMethod(0x06000000 + (int)rid);
				var stackTrace = StackTracePatcher.WriteStackFrame(new StackTrace(), 1, encMethod);
				if (!decryptMethod(Environment.Version.Major, appDomainId, asmHashCode, methodId, stackTrace, MyDecryptCallback))
					return null;
				return decryptedData;
			}

			unsafe bool MyDecryptCallback(byte* pMethodCode, int methodSize, int methodId) {
				decryptedData = new byte[methodSize];
				Marshal.Copy(new IntPtr(pMethodCode), decryptedData, 0, decryptedData.Length);
				return true;
			}
		}

		abstract class DecrypterBaseV2_0_12_x : IDecrypter {
			protected readonly DynamicMethodsDecrypter dmd;
			protected byte[] currentILBytes;
			byte[] decryptedData;
			readonly Delegate invoker;
			protected readonly IntPtr pGetILBytes;
			protected readonly IntPtr pDecryptCallback;

			protected unsafe DecrypterBaseV2_0_12_x(DynamicMethodsDecrypter dmd) {
				this.dmd = dmd;
				this.invoker = (Delegate)dmd.invokerFieldInfo.GetValue(null);

				byte* p = (byte*)GetStateAddr(invoker.Target);
				p += IntPtr.Size * 3;
				p = *(byte**)p;
				p += 8 + IntPtr.Size * 8;
				p = *(byte**)p;
				p += IntPtr.Size * 3;
				p = *(byte**)p;
				pGetILBytes = new IntPtr(p + IntPtr.Size * 39);
				pDecryptCallback = new IntPtr(p + IntPtr.Size * 40);
			}

			public static IntPtr GetStateAddr(object obj) {
				var flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
				foreach (var fi in obj.GetType().GetFields(flags)) {
					if (fi.FieldType == typeof(IntPtr))
						return (IntPtr)fi.GetValue(obj);
				}
				throw new ApplicationException("Could not find an IntPtr field");
			}

			public byte[] Decrypt(int methodId, uint rid) {
				decryptedData = null;
				currentILBytes = dmd.reflectionModule.ResolveMethod(0x06000000 + (int)rid).GetMethodBody().GetILAsByteArray();
				invoker.DynamicInvoke(new object[1] { methodId });
				return decryptedData;
			}

			protected unsafe void SaveDecryptedData(byte* pMethodCode, int methodSize) {
				decryptedData = new byte[methodSize];
				Marshal.Copy(new IntPtr(pMethodCode), decryptedData, 0, decryptedData.Length);
			}
		}

		// 2.0.12.0 - 2.0.12.2
		class DecrypterV2_0_12_0 : DecrypterBaseV2_0_12_x {
			readonly GetCallerMethodAsILByteArrayDelegate getCallerMethodAsILByteArrayDelegate;
			readonly DecryptCallbackDelegate decryptCallbackDelegate;

			[return: MarshalAs(UnmanagedType.SafeArray)]
			delegate byte[] GetCallerMethodAsILByteArrayDelegate(IntPtr a, int skipFrames);
			unsafe delegate bool DecryptCallbackDelegate(IntPtr a, byte* pMethodCode, int methodSize, int methodId);

			public unsafe DecrypterV2_0_12_0(DynamicMethodsDecrypter dmd)
				: base(dmd) {
				getCallerMethodAsILByteArrayDelegate = GetCallerMethodAsILByteArray;
				decryptCallbackDelegate = DecryptCallback;

				*(IntPtr*)pGetILBytes = Marshal.GetFunctionPointerForDelegate(getCallerMethodAsILByteArrayDelegate);
				*(IntPtr*)pDecryptCallback = Marshal.GetFunctionPointerForDelegate(decryptCallbackDelegate);
			}

			byte[] GetCallerMethodAsILByteArray(IntPtr a, int skipFrames) {
				return currentILBytes;
			}

			unsafe bool DecryptCallback(IntPtr a, byte* pMethodCode, int methodSize, int methodId) {
				SaveDecryptedData(pMethodCode, methodSize);
				return true;
			}
		}

		// 2.0.12.3
		class DecrypterV2_0_12_3 : DecrypterBaseV2_0_12_x {
			readonly GetCallerMethodAsILByteArrayDelegate getCallerMethodAsILByteArrayDelegate;
			readonly DecryptCallbackDelegate decryptCallbackDelegate;

			[return: MarshalAs(UnmanagedType.SafeArray)]
			delegate byte[] GetCallerMethodAsILByteArrayDelegate(IntPtr a, int skipFrames, IntPtr c, IntPtr d);
			unsafe delegate bool DecryptCallbackDelegate(IntPtr a, byte* pMethodCode, int methodSize, int methodId, IntPtr e);

			public unsafe DecrypterV2_0_12_3(DynamicMethodsDecrypter dmd)
				: base(dmd) {
				getCallerMethodAsILByteArrayDelegate = GetCallerMethodAsILByteArray;
				decryptCallbackDelegate = DecryptCallback;

				*(IntPtr*)pGetILBytes = Marshal.GetFunctionPointerForDelegate(getCallerMethodAsILByteArrayDelegate);
				*(IntPtr*)pDecryptCallback = Marshal.GetFunctionPointerForDelegate(decryptCallbackDelegate);
			}

			byte[] GetCallerMethodAsILByteArray(IntPtr a, int skipFrames, IntPtr c, IntPtr d) {
				return currentILBytes;
			}

			unsafe bool DecryptCallback(IntPtr a, byte* pMethodCode, int methodSize, int methodId, IntPtr e) {
				SaveDecryptedData(pMethodCode, methodSize);
				return true;
			}
		}

		abstract class DecrypterV2_0_13_0_Base : IDecrypter {
			protected readonly DynamicMethodsDecrypter dmd;
			protected byte[] currentILBytes;
			byte[] decryptedData;
			readonly Delegate invoker;

			readonly GetCallerMethodAsILByteArrayDelegate getCallerMethodAsILByteArrayDelegate;
			readonly DecryptCallbackDelegate decryptCallbackDelegate;
			readonly IgnoreDelegate ignoreDelegate;

			[DllImport("kernel32")]
			static extern bool GetModuleHandleEx(uint dwFlags, IntPtr lpModuleName, out IntPtr phModule);

			[return: MarshalAs(UnmanagedType.SafeArray)]
			delegate byte[] GetCallerMethodAsILByteArrayDelegate(IntPtr a, int skipFrames, ref int flags, IntPtr d);
			unsafe delegate bool DecryptCallbackDelegate(IntPtr a, byte* pMethodCode, int methodSize, int methodId, IntPtr e);
			delegate IntPtr IgnoreDelegate(IntPtr a, IntPtr b);

			public unsafe DecrypterV2_0_13_0_Base(DynamicMethodsDecrypter dmd) {
				this.dmd = dmd;
				this.invoker = (Delegate)dmd.invokerFieldInfo.GetValue(null);

				byte* p = (byte*)DecrypterBaseV2_0_12_x.GetStateAddr(invoker.Target);
				byte* pis = GetAddr(*(byte**)p);
				p = *(byte**)pis;
				byte* pam = *(byte**)(p + IntPtr.Size * 2);
				p = *(byte**)(p + ((Environment.Version.Major - 2) / 2 * IntPtr.Size));
				p += IntPtr.Size * 8 + 0x18;
				p = LookUp(p, AppDomain.CurrentDomain.Id);
				p = *(byte**)(p + IntPtr.Size * 16 + 0x18);
				byte* pd = p + IntPtr.Size * 2;
				p = *(byte**)(p + IntPtr.Size * 13);

				getCallerMethodAsILByteArrayDelegate = GetCallerMethodAsILByteArray;
				decryptCallbackDelegate = DecryptCallback;
				ignoreDelegate = IgnoreMethod;

				byte* pm = p + 0x28 * IntPtr.Size;
				*(IntPtr*)(p + 0x29 * IntPtr.Size) = Marshal.GetFunctionPointerForDelegate(getCallerMethodAsILByteArrayDelegate);
				*(IntPtr*)(p + 0x2A * IntPtr.Size) = Marshal.GetFunctionPointerForDelegate(decryptCallbackDelegate);
				if (IntPtr.Size == 4)
					*(IntPtr*)(p + 0x2B * IntPtr.Size) = Marshal.GetFunctionPointerForDelegate(ignoreDelegate);
				InitCode(GetModuleHandle(pis), pam, pd, pm);
			}

			static unsafe byte* GetModuleHandle(byte* addr) {
				IntPtr hModule;
				if (!GetModuleHandleEx(4, new IntPtr(addr), out hModule))
					throw new ApplicationException("GetModuleHandleEx() failed");
				return (byte*)hModule;
			}

			protected unsafe abstract void InitCode(byte* ba, byte* pam, byte* pd, byte* pm);

			static unsafe byte* GetAddr(byte* p) {
				if (IntPtr.Size == 4) {
					for (int i = 0; i < 20; i++, p++) {
						if (*p == 0xA1)
							return *(byte**)(p + 1);
					}
				}
				else {
					for (int i = 0; i < 20; i++, p++)
						if (*p == 0x4C && p[1] == 0x8B && p[2] == 0x15)
							return p + 7 + *(int*)(p + 3);
				}
				return null;
			}

			static unsafe byte* LookUp(byte* p, int key) {
				p = *(byte**)(p + IntPtr.Size);
				p = *(byte**)(p + IntPtr.Size);

				int f1 = 0;
				int f2 = IntPtr.Size * 2;
				int f3 = IntPtr.Size * 3;
				int f4 = IntPtr.Size * 4;
				int f5 = IntPtr.Size * 5 + 1;

				byte* res = null;
				while (true) {
					if (*(p + f5) != 0)
						break;
					int k = *(int*)(p + f3);
					if (k < key)
						p = *(byte**)(p + f2);
					else {
						res = p;
						p = *(byte**)(p + f1);
					}
				}
				return *(byte**)(res + f4);
			}

			byte[] aryDummy = new byte[7];
			IntPtr dummy;
			public unsafe byte[] Decrypt(int methodId, uint rid) {
				fixed (byte* p = &aryDummy[0]) {
					dummy = new IntPtr(p);
					decryptedData = null;
					currentILBytes = dmd.reflectionModule.ResolveMethod(0x06000000 + (int)rid).GetMethodBody().GetILAsByteArray();
					invoker.DynamicInvoke(new object[1] { methodId });
				}
				dummy = IntPtr.Zero;
				return decryptedData;
			}

			byte[] GetCallerMethodAsILByteArray(IntPtr a, int skipFrames, ref int flags, IntPtr d) {
				flags = 2;
				return currentILBytes;
			}

			unsafe bool DecryptCallback(IntPtr a, byte* pMethodCode, int methodSize, int methodId, IntPtr e) {
				decryptedData = new byte[methodSize];
				Marshal.Copy(new IntPtr(pMethodCode), decryptedData, 0, decryptedData.Length);
				return true;
			}

			IntPtr IgnoreMethod(IntPtr a, IntPtr b) {
				return dummy;
			}
		}

		class DecrypterV2_0_13_0 : DecrypterV2_0_13_0_Base {
			public unsafe DecrypterV2_0_13_0(DynamicMethodsDecrypter dmd)
				: base(dmd) {
			}

			static readonly byte[] initCode_x86 = new byte[] {
				0x8B, 0xCC, 0x8B, 0x41, 0x04, 0xFF, 0x71, 0x10,
				0xFF, 0x71, 0x0C, 0xFF, 0x71, 0x08, 0xFF, 0x51,
				0x14, 0xC2, 0x14, 0x00,
			};
			unsafe delegate void InitCode32Delegate(byte* pppam, byte* m, IntPtr s, byte* pd, byte* f);
			unsafe delegate void InitCode64Delegate(byte* pppam, byte* m, IntPtr s, byte* pd);
			protected unsafe override void InitCode(byte* ba, byte* pam, byte* pd, byte* pm) {
				byte* ppam = (byte*)&pam;
				byte* pppam = (byte*)&ppam;
				if (IntPtr.Size == 4) {
					var del = (InitCode32Delegate)Marshal.GetDelegateForFunctionPointer(CodeAllocator.Allocate(initCode_x86), typeof(InitCode32Delegate));
					del(pppam, pm, new IntPtr(IntPtr.Size * 4), pd, ba + 0x00012500);
				}
				else {
					var del = (InitCode64Delegate)Marshal.GetDelegateForFunctionPointer(new IntPtr(ba + 0x00014CF0), typeof(InitCode64Delegate));
					del(pppam, pm, new IntPtr(IntPtr.Size * 4), pd);
				}
			}
		}

		class DecrypterV2_0_13_1 : DecrypterV2_0_13_0_Base {
			public unsafe DecrypterV2_0_13_1(DynamicMethodsDecrypter dmd)
				: base(dmd) {
			}

			unsafe delegate void InitCodeDelegate(byte* pppam, byte* m, IntPtr s, byte* pd);
			protected unsafe override void InitCode(byte* ba, byte* pam, byte* pd, byte* pm) {
				int rva = IntPtr.Size == 4 ? 0x00013650 : 0x00016B50;
				var del = (InitCodeDelegate)Marshal.GetDelegateForFunctionPointer(new IntPtr(ba + rva), typeof(InitCodeDelegate));
				byte* ppam = (byte*)&pam;
				byte* pppam = (byte*)&ppam;
				del(pppam, pm, new IntPtr(IntPtr.Size * 4), pd);
			}
		}

		public bool MethodReaderHasDelegateTypeFlag {
			get { return methodReaderHasDelegateTypeFlag; }
		}

		public DynamicMethodsDecrypter(ModuleDefMD module, Module reflectionModule) {
			this.module = module;
			this.reflectionModule = reflectionModule;
		}

		public void Initialize() {
			RuntimeHelpers.RunModuleConstructor(reflectionModule.ModuleHandle);
			var reflectionProtectAssembly = GetProtectAssembly();
			if (reflectionProtectAssembly == null)
				throw new ApplicationException("Could not find 'Protect' assembly");
			reflectionProtectModule = reflectionProtectAssembly.ManifestModule;
			moduleProtect = ModuleDefMD.Load(reflectionProtectModule);
			protectMainType = FindMainType(moduleProtect);
			if (protectMainType == null)
				throw new ApplicationException("Could not find Protect.MainType");
			var invokerField = FindInvokerField(module);

			/*reflectionProtectMainType =*/ reflectionProtectModule.ResolveType(0x02000000 + (int)protectMainType.Rid);
			invokerFieldInfo = reflectionModule.ResolveField(0x04000000 + (int)invokerField.Rid);

			decrypter = CreateDecrypter();
			if (decrypter == null)
				throw new ApplicationException("Probably a new version. Could not create a decrypter.");
		}

		public DecryptedMethodInfo Decrypt(int methodId, uint rid) {
			byte[] methodData = decrypter.Decrypt(methodId, rid);
			if (methodData == null)
				throw new ApplicationException(string.Format("Probably a new version. Could not decrypt method. ID:{0}, RID:{1:X4}", methodId, rid));
			return new DecryptedMethodInfo(methodId, methodData);
		}

		public void Dispose() {
			if (moduleProtect != null)
				moduleProtect.Dispose();
			moduleProtect = null;
		}

		IDecrypter CreateDecrypter() {
			var version = reflectionProtectModule.Assembly.GetName().Version;
			if (reflectionProtectModule.Assembly.GetName().Version < new Version(2, 0, 12, 0)) {
				return CreateDecrypterV1_0_7_0() ??
					CreateDecrypterV2_0_0_0() ??
					CreateDecrypterV2_0_8_0() ??
					CreateDecrypterV2_0_8_5() ??
					CreateDecrypterV2_0_9_0();
			}

			methodReaderHasDelegateTypeFlag = true;
			if (version < new Version(2, 0, 12, 3))
				return new DecrypterV2_0_12_0(this);
			if (version == new Version(2, 0, 12, 3))
				return new DecrypterV2_0_12_3(this);
			if (version == new Version(2, 0, 13, 0))
				return new DecrypterV2_0_13_0(this);
			if (version == new Version(2, 0, 13, 1))
				return new DecrypterV2_0_13_1(this);

			return null;
		}

		IDecrypter CreateDecrypterV1_0_7_0() {
			var delegateField = FindDelegateFieldV1_0_7_0(protectMainType);
			if (delegateField == null)
				return null;

			return new DecrypterV1_0_7_0(this, delegateField);
		}

		IDecrypter CreateDecrypterV2_0_0_0() {
			var delegateField = FindDelegateFieldV2_0_0_0(protectMainType);
			if (delegateField == null)
				return null;

			return new DecrypterV2_0_0_0(this, delegateField);
		}

		IDecrypter CreateDecrypterV2_0_8_0() {
			var delegateField = FindDelegateFieldV2_0_8_0(protectMainType, FindDecryptCallbackV2_0_8_0(protectMainType));
			if (delegateField == null)
				return null;

			return new DecrypterV2_0_8_0(this, delegateField);
		}

		IDecrypter CreateDecrypterV2_0_8_5() {
			var delegateField = FindDelegateFieldV2_0_8_5(protectMainType, FindDecryptCallbackV2_0_8_0(protectMainType));
			if (delegateField == null)
				return null;

			return new DecrypterV2_0_8_5(this, delegateField);
		}

		IDecrypter CreateDecrypterV2_0_9_0() {
			var delegateField = FindDelegateFieldV2_0_9_0(protectMainType, FindDecryptCallbackV2_0_9_0(protectMainType));
			if (delegateField == null)
				return null;

			return new DecrypterV2_0_9_0(this, delegateField);
		}

		static readonly byte[] ilpPublicKeyToken = new byte[8] { 0x20, 0x12, 0xD3, 0xC0, 0x55, 0x1F, 0xE0, 0x3D };
		static Assembly GetProtectAssembly() {
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
				if (!string.IsNullOrEmpty(asm.Location))
					continue;
				var asmName = asm.GetName();
				if (asmName.Name != "Protect")
					continue;
				if (!Compare(asmName.GetPublicKeyToken(), ilpPublicKeyToken))
					continue;

				return asm;
			}
			return null;
		}

		static bool Compare(byte[] a, byte[] b) {
			if (a == null && b == null)
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

		static TypeDef FindMainType(ModuleDef module) {
			foreach (var type in module.Types) {
				if (type.FindMethod("Finalize") != null)
					return type;
			}
			return null;
		}

		static FieldDef FindInvokerField(ModuleDef module) {
			return FindDelegateField(module.GlobalType, "System.Delegate", "(System.Int32)");
		}

		static FieldDef FindDelegateFieldV1_0_7_0(TypeDef mainType) {
			return FindDelegateField(mainType, "System.Boolean", "(System.Int32,System.Int32,System.Int32,System.Byte*&,System.Int32&)");
		}

		static FieldDef FindDelegateFieldV2_0_0_0(TypeDef mainType) {
			return FindDelegateField(mainType, "System.Boolean", "(System.Int32,System.Int32,System.Int32,System.Int32,System.Byte*&,System.Int32&)");
		}

		static FieldDef FindDecryptCallbackV2_0_8_0(TypeDef mainType) {
			return FindDelegateField(mainType, "System.Boolean", "(System.Byte*,System.Int32,System.Delegate&)");
		}

		static FieldDef FindDecryptCallbackV2_0_9_0(TypeDef mainType) {
			return FindDelegateField(mainType, "System.Boolean", "(System.Byte*,System.Int32,System.Int32)");
		}

		static FieldDef FindDelegateFieldV2_0_8_0(TypeDef mainType, FieldDef decryptCallbackField) {
			if (decryptCallbackField == null)
				return null;
			var type = decryptCallbackField.FieldSig.GetFieldType().ToTypeDefOrRef() as TypeDef;
			if (type == null)
				return null;
			return FindDelegateField(mainType, "System.Boolean", string.Format("(System.Int32,System.Int32,System.Int32,System.Int32,{0},System.Delegate&)", type.FullName));
		}

		static FieldDef FindDelegateFieldV2_0_8_5(TypeDef mainType, FieldDef decryptCallbackField) {
			if (decryptCallbackField == null)
				return null;
			var type = decryptCallbackField.FieldSig.GetFieldType().ToTypeDefOrRef() as TypeDef;
			if (type == null)
				return null;
			return FindDelegateField(mainType, "System.Boolean", string.Format("(System.Int32,System.Int32,System.Int32,System.Int32,System.Diagnostics.StackTrace,{0},System.Delegate&)", type.FullName));
		}

		static FieldDef FindDelegateFieldV2_0_9_0(TypeDef mainType, FieldDef decryptCallbackField) {
			if (decryptCallbackField == null)
				return null;
			var type = decryptCallbackField.FieldSig.GetFieldType().ToTypeDefOrRef() as TypeDef;
			if (type == null)
				return null;
			return FindDelegateField(mainType, "System.Boolean", string.Format("(System.Int32,System.Int32,System.Int32,System.Int32,System.Diagnostics.StackTrace,{0})", type.FullName));
		}

		static FieldDef FindDelegateField(TypeDef mainType, string returnType, string parameters) {
			foreach (var field in mainType.Fields) {
				var fieldSig = field.FieldSig;
				if (fieldSig == null)
					continue;
				var fieldType = fieldSig.Type.ToTypeDefOrRef() as TypeDef;
				if (fieldType == null)
					continue;
				if (fieldType.BaseType != null && fieldType.BaseType.FullName != "System.MulticastDelegate")
					continue;
				var invokeMethod = fieldType.FindMethod("Invoke");
				if (!DotNetUtils.IsMethod(invokeMethod, returnType, parameters))
					continue;

				return field;
			}
			return null;
		}
	}
}
