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
using System.Text;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class AssemblyResolver {
		ModuleDefMD module;
		DecrypterType decrypterType;
		TypeDef resolverType;
		MethodDef initMethod;
		MethodDef handlerMethod;
		MethodDef decryptMethod;
		TypeDef otherType;
		List<AssemblyInfo> assemblyInfos = new List<AssemblyInfo>();
		FrameworkType frameworkType;
		byte[] decryptKey;
		CodeCompilerMethodCallRestorer codeCompilerMethodCallRestorer;

		public class AssemblyInfo {
			public bool IsEncrypted { get; set; }
			public bool IsCompressed { get; set; }
			public string ResourceName { get; set; }
			public string Filename { get; set; }
			public string AssemblyFullName { get; set; }
			public string SimpleName { get; set; }
			public string Extension { get; set; }
			public EmbeddedResource Resource { get; set; }
			public byte[] Data { get; set; }
			public override string ToString() => AssemblyFullName ?? Filename;
		}

		public TypeDef Type => resolverType;
		public TypeDef OtherType => otherType;
		public MethodDef InitMethod => initMethod;
		public IEnumerable<AssemblyInfo> AssemblyInfos => assemblyInfos;
		public bool Detected => resolverType != null;

		public AssemblyResolver(ModuleDefMD module, DecrypterType decrypterType) {
			this.module = module;
			frameworkType = DotNetUtils.GetFrameworkType(module);
			this.decrypterType = decrypterType;
			codeCompilerMethodCallRestorer = new CodeCompilerMethodCallRestorer(module);
		}

		public void Find() => CheckCalledMethods(DotNetUtils.GetModuleTypeCctor(module));

		bool CheckCalledMethods(MethodDef method) {
			if (method == null || method.Body == null)
				return false;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;

				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null || !calledMethod.IsStatic || calledMethod.Body == null)
					continue;
				if (!DotNetUtils.IsMethod(calledMethod, "System.Void", "()"))
					continue;

				if (frameworkType == FrameworkType.Silverlight) {
					if (!CheckInitMethodSilverlight(calledMethod))
						continue;
				}
				else {
					if (!CheckInitMethod(calledMethod))
						continue;
				}

				decryptMethod = GetDecryptMethod();
				UpdateDecrypterType();
				FindCodeDomMethods();
				return true;
			}

			return false;
		}

		bool CheckInitMethodSilverlight(MethodDef method) {
			var type = method.DeclaringType;
			if (type.NestedTypes.Count != 2)
				return false;

			var resolveHandler = GetResolveMethodSilverlight(method);
			if (resolveHandler == null)
				return false;

			initMethod = method;
			resolverType = type;
			handlerMethod = resolveHandler;
			return true;
		}

		static MethodDef GetResolveMethodSilverlight(MethodDef initMethod) {
			foreach (var instr in initMethod.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null)
					continue;
				if (!DotNetUtils.IsMethod(calledMethod, "System.Void", "()"))
					continue;
				if (!DeobUtils.HasInteger(calledMethod, ',') ||
					!DeobUtils.HasInteger(calledMethod, '|'))
					continue;

				return calledMethod;
			}

			return null;
		}

		bool CheckInitMethod(MethodDef method) {
			var type = method.DeclaringType;
			if (type.NestedTypes.Count < 2 || type.NestedTypes.Count > 6)
				return false;
			if (DotNetUtils.GetPInvokeMethod(type, "kernel32", "MoveFileEx") == null)
				return false;

			var resolveHandler = DeobUtils.GetResolveMethod(method);
			if (resolveHandler == null)
				return false;
			if (!DeobUtils.HasInteger(resolveHandler, ',') ||
				!DeobUtils.HasInteger(resolveHandler, '|'))
				return false;

			initMethod = method;
			resolverType = type;
			handlerMethod = resolveHandler;
			return true;
		}

		MethodDef GetDecryptMethod() {
			foreach (var method in resolverType.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Byte[]", "(System.Byte[])"))
					continue;
				if (!DeobUtils.HasInteger(method, 32) ||
					!DeobUtils.HasInteger(method, 121))
					continue;

				return method;
			}

			return null;
		}

		void UpdateDecrypterType() {
			var theDecrypterType = GetDecrypterType(decryptMethod);
			if (theDecrypterType == null)
				return;
			decrypterType.Type = theDecrypterType;
			if (!decrypterType.Initialize())
				throw new ApplicationException("Could not initialize decrypterType");
		}

		TypeDef GetDecrypterType(MethodDef method) {
			if (method == null || method.Body == null)
				return null;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null || !calledMethod.IsStatic || calledMethod.DeclaringType == resolverType)
					continue;
				if (!DotNetUtils.IsMethod(calledMethod, "System.Void", "(System.Byte[])"))
					continue;

				return calledMethod.DeclaringType;
			}

			return null;
		}

		public void Initialize(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			if (handlerMethod == null)
				return;

			FindOtherType();

			simpleDeobfuscator.Deobfuscate(handlerMethod);
			simpleDeobfuscator.DecryptStrings(handlerMethod, deob);
			if (!CreateAssemblyInfos())
				throw new ApplicationException("Could not initialize assembly infos");

			if (decryptMethod != null) {
				simpleDeobfuscator.Deobfuscate(decryptMethod);
				simpleDeobfuscator.DecryptStrings(decryptMethod, deob);
				if (!CreateDecryptKey())
					throw new ApplicationException("Could not initialize decryption key");
			}
		}

		void FindOtherType() {
			foreach (var type in module.Types) {
				// This type is added by EF 3.1+. The last number seems to be an int32 hash of
				// the assembly name, but - replaced with _.
				if (!Regex.IsMatch(type.FullName, @"^pc1eOx2WJVV[_0-9]+$"))
					continue;

				otherType = type;
				break;
			}
		}

		bool CreateDecryptKey() {
			if (decryptMethod == null)
				return false;

			foreach (var s in DotNetUtils.GetCodeStrings(decryptMethod)) {
				decryptKey = DecodeBase64(s);
				if (decryptKey == null || decryptKey.Length == 0)
					continue;

				if (decrypterType.Detected) {
					var data = new byte[8];
					ulong magic = decrypterType.GetMagic();
					data[0] = (byte)magic;
					data[7] = (byte)(magic >> 8);
					data[6] = (byte)(magic >> 16);
					data[5] = (byte)(magic >> 24);
					data[4] = (byte)(magic >> 32);
					data[1] = (byte)(magic >> 40);
					data[3] = (byte)(magic >> 48);
					data[2] = (byte)(magic >> 56);

					for (int i = 0; i < decryptKey.Length; i++)
						decryptKey[i] ^= (byte)(i + data[i % data.Length]);
				}

				return true;
			}

			decryptKey = null;
			return false;
		}

		static byte[] DecodeBase64(string s) {
			try {
				return Convert.FromBase64String(s);
			}
			catch (FormatException) {
				return null;
			}
		}

		bool CreateAssemblyInfos() {
			int numElements = DeobUtils.HasInteger(handlerMethod, 3) ? 3 : 2;
			foreach (var s in DotNetUtils.GetCodeStrings(handlerMethod)) {
				var infos = CreateAssemblyInfos(s, numElements);
				if (infos == null)
					continue;

				assemblyInfos = infos;
				return true;
			}

			return false;
		}

		List<AssemblyInfo> CreateAssemblyInfos(string s, int numElements) {
			try {
				return TryCreateAssemblyInfos(s, numElements);
			}
			catch (FormatException) {
				return null;	// Convert.FromBase64String() failed
			}
		}

		List<AssemblyInfo> TryCreateAssemblyInfos(string s, int numElements) {
			var ary = s.Split(',');
			if (ary.Length == 0 || ary.Length % numElements != 0)
				return null;

			var infos = new List<AssemblyInfo>();
			for (int i = 0; i < ary.Length; i += numElements) {
				var info = new AssemblyInfo();

				info.AssemblyFullName = Encoding.UTF8.GetString(Convert.FromBase64String(ary[i]));
				info.ResourceName = ary[i + 1];
				if (numElements >= 3)
					info.Filename = Encoding.UTF8.GetString(Convert.FromBase64String(ary[i + 2]));
				else
					info.Filename = Utils.GetAssemblySimpleName(info.AssemblyFullName) + ".dll";
				int index = info.ResourceName.IndexOf('|');
				if (index >= 0) {
					var flags = info.ResourceName.Substring(0, index);
					info.ResourceName = info.ResourceName.Substring(index + 1);
					info.IsEncrypted = flags.IndexOf('a') >= 0;
					info.IsCompressed = flags.IndexOf('b') >= 0;
				}

				infos.Add(info);
			}

			return infos;
		}

		public void InitializeEmbeddedFiles() {
			foreach (var info in assemblyInfos) {
				info.Resource = DotNetUtils.GetResource(module, info.ResourceName) as EmbeddedResource;
				if (info.Resource == null)
					throw new ApplicationException($"Could not find resource {Utils.ToCsharpString(info.ResourceName)}");

				info.Data = info.Resource.CreateReader().ToArray();
				if (info.IsEncrypted)
					Decrypt(info.Data);
				if (info.IsCompressed)
					info.Data = Decompress(info.Data);

				InitializeNameAndExtension(info);
			}
		}

		static void InitializeNameAndExtension(AssemblyInfo info) {
			try {
				var mod = ModuleDefMD.Load(info.Data);
				info.AssemblyFullName = mod.Assembly.FullName;
				info.SimpleName = mod.Assembly.Name.String;
				info.Extension = DeobUtils.GetExtension(mod.Kind);
				return;
			}
			catch {
			}
			Logger.w("Could not load assembly from decrypted resource {0}", Utils.ToCsharpString(info.ResourceName));
			int index = info.Filename.LastIndexOf('.');
			if (index < 0) {
				info.SimpleName = info.Filename;
				info.Extension = "";
			}
			else {
				info.SimpleName = info.Filename.Substring(0, index);
				info.Extension = info.Filename.Substring(index);
			}
		}

		static readonly byte[] key2 = new byte[] { 148, 68, 208, 52 };
		void Decrypt(byte[] encryptedData) {
			var indexes = new byte[256];
			for (int i = 0; i < indexes.Length; i++)
				indexes[i] = (byte)i;
			byte i1 = 0, i2 = 0;
			for (int i = 0; i < indexes.Length; i++) {
				i2 += (byte)(decryptKey[i % decryptKey.Length] + indexes[i]);
				Swap(indexes, i, i2);
			}

			byte val = 0;
			for (int i = 0; i < encryptedData.Length; i++) {
				if ((i & 0x1F) == 0) {
					i2 += indexes[++i1];
					Swap(indexes, i1, i2);
					val = indexes[(byte)(indexes[i1] + indexes[i2])];
				}
				encryptedData[i] ^= (byte)(val ^ key2[(i >> 2) & 3] ^ key2[(i + 1) & 3]);
			}
		}

		static void Swap(byte[] data, int i, int j) {
			byte tmp = data[i];
			data[i] = data[j];
			data[j] = tmp;
		}

		byte[] Decompress(byte[] compressedData) {
			// First dword is sig: 0x9B728BC7
			// Second dword is decompressed length
			return DeobUtils.Inflate(compressedData, 8, compressedData.Length - 8, true);
		}

		public AssemblyInfo Get(string asmFullName) {
			var simpleName = Utils.GetAssemblySimpleName(asmFullName);
			for (int i = 0; i < assemblyInfos.Count; i++) {
				var info = assemblyInfos[i];
				if (info.SimpleName != simpleName)
					continue;

				assemblyInfos.RemoveAt(i);
				return info;
			}

			return null;
		}

		public void Deobfuscate(Blocks blocks) => codeCompilerMethodCallRestorer.Deobfuscate(blocks);

		void FindCodeDomMethods() {
			if (resolverType == null)
				return;

			foreach (var nestedType in resolverType.NestedTypes) {
				if (nestedType.Fields.Count != 0)
					continue;

				var CompileAssemblyFromDom1         = GetTheOnlyMethod(nestedType, "System.CodeDom.Compiler.CodeDomProvider", "CompileAssemblyFromDom", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.CodeDom.CodeCompileUnit[]");
				var CompileAssemblyFromFile1        = GetTheOnlyMethod(nestedType, "System.CodeDom.Compiler.CodeDomProvider", "CompileAssemblyFromFile", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.String[]");
				var CompileAssemblyFromSource1      = GetTheOnlyMethod(nestedType, "System.CodeDom.Compiler.CodeDomProvider", "CompileAssemblyFromSource", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.String[]");
				var CompileAssemblyFromDom2         = GetTheOnlyMethod(nestedType, "System.CodeDom.Compiler.ICodeCompiler", "CompileAssemblyFromDom", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.CodeDom.CodeCompileUnit");
				var CompileAssemblyFromDomBatch2    = GetTheOnlyMethod(nestedType, "System.CodeDom.Compiler.ICodeCompiler", "CompileAssemblyFromDomBatch", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.CodeDom.CodeCompileUnit[]");
				var CompileAssemblyFromFile2        = GetTheOnlyMethod(nestedType, "System.CodeDom.Compiler.ICodeCompiler", "CompileAssemblyFromFile", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.String");
				var CompileAssemblyFromFileBatch2   = GetTheOnlyMethod(nestedType, "System.CodeDom.Compiler.ICodeCompiler", "CompileAssemblyFromFileBatch", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.String[]");
				var CompileAssemblyFromSource2      = GetTheOnlyMethod(nestedType, "System.CodeDom.Compiler.ICodeCompiler", "CompileAssemblyFromSource", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.String");
				var CompileAssemblyFromSourceBatch2 = GetTheOnlyMethod(nestedType, "System.CodeDom.Compiler.ICodeCompiler", "CompileAssemblyFromSourceBatch", "System.CodeDom.Compiler.CompilerResults", "System.CodeDom.Compiler.CompilerParameters,System.String[]");

				if (CompileAssemblyFromDom1 == null && CompileAssemblyFromFile1 == null &&
					CompileAssemblyFromSource1 == null && CompileAssemblyFromDom2 == null &&
					CompileAssemblyFromDomBatch2 == null && CompileAssemblyFromFile2 == null &&
					CompileAssemblyFromFileBatch2 == null && CompileAssemblyFromSource2 == null &&
					CompileAssemblyFromSourceBatch2 == null) {
					continue;
				}

				codeCompilerMethodCallRestorer.Add_CodeDomProvider_CompileAssemblyFromDom(CompileAssemblyFromDom1);
				codeCompilerMethodCallRestorer.Add_CodeDomProvider_CompileAssemblyFromFile(CompileAssemblyFromFile1);
				codeCompilerMethodCallRestorer.Add_CodeDomProvider_CompileAssemblyFromSource(CompileAssemblyFromSource1);
				codeCompilerMethodCallRestorer.Add_ICodeCompiler_CompileAssemblyFromDom(CompileAssemblyFromDom2);
				codeCompilerMethodCallRestorer.Add_ICodeCompiler_CompileAssemblyFromDomBatch(CompileAssemblyFromDomBatch2);
				codeCompilerMethodCallRestorer.Add_ICodeCompiler_CompileAssemblyFromFile(CompileAssemblyFromFile2);
				codeCompilerMethodCallRestorer.Add_ICodeCompiler_CompileAssemblyFromFileBatch(CompileAssemblyFromFileBatch2);
				codeCompilerMethodCallRestorer.Add_ICodeCompiler_CompileAssemblyFromSource(CompileAssemblyFromSource2);
				codeCompilerMethodCallRestorer.Add_ICodeCompiler_CompileAssemblyFromSourceBatch(CompileAssemblyFromSourceBatch2);
				break;
			}
		}

		static MethodDef GetTheOnlyMethod(TypeDef type, string typeName, string methodName, string returnType, string parameters) {
			MethodDef foundMethod = null;

			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null || method.HasGenericParameters)
					continue;
				if (method.IsPrivate)
					continue;
				if (!DotNetUtils.IsMethod(method, returnType, "(" + typeName + "," + parameters + ")"))
					continue;
				if (!DotNetUtils.CallsMethod(method, returnType + " " + typeName + "::" + methodName + "(" + parameters + ")"))
					continue;

				if (foundMethod != null)
					return null;
				foundMethod = method;
			}

			return foundMethod;
		}
	}
}
