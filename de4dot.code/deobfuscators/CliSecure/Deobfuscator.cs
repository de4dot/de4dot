/*
    Copyright (C) 2011 de4dot@gmail.com

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
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.deobfuscators.CliSecure {
	class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "CliSecure";
		const string DEFAULT_REGEX = @"[a-zA-Z_0-9>}$]$";
		BoolOption fixResources;
		BoolOption removeStackFrameHelper;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			fixResources = new BoolOption(null, makeArgName("rsrc"), "Decrypt resources", true);
			removeStackFrameHelper = new BoolOption(null, makeArgName("stack"), "Remove all StackFrameHelper code", true);
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return "cs"; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
				FixResources = fixResources.get(),
				RemoveStackFrameHelper = removeStackFrameHelper.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				fixResources,
				removeStackFrameHelper,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		bool foundCliSecureAttribute = false;
		bool foundProxyDelegateMethod = false;

		MethodDefinition decryptStringMethod;
		byte[] decryptStringBuffer;

		ProxyDelegateFinder proxyDelegateFinder;

		TypeDefinition cliSecureRtType;
		MethodDefinition postInitializeMethod;
		MethodDefinition initializeMethod;
		TypeDefinition stackFrameHelperType;

		ExceptionLoggerRemover exceptionLoggerRemover = new ExceptionLoggerRemover();

		MethodDefinition rsrcRrrMethod;
		MethodDefinition rsrcDecryptMethod;

		internal class Options : OptionsBase {
			public bool FixResources { get; set; }
			public bool RemoveStackFrameHelper { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return Type; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		public override void init(ModuleDefinition module) {
			base.init(module);
		}

		protected override int detectInternal() {
			int val = 0;

			if (cliSecureRtType != null || foundCliSecureAttribute)
				val += 100;
			if (decryptStringBuffer != null)
				val += 10;
			if (foundProxyDelegateMethod)
				val += 10;

			return val;
		}

		protected override void scanForObfuscator() {
			proxyDelegateFinder = new ProxyDelegateFinder(module);
			findCliSecureAttribute();
			findCliSecureRtType();
			findStringDecryptBuffer();
			findDelegateCreatorType();
		}

		void findCliSecureAttribute() {
			foreach (var type in module.Types) {
				if (type.FullName == "SecureTeam.Attributes.ObfuscatedByCliSecureAttribute") {
					this.addAttributeToBeRemoved(type, "Obfuscator attribute");
					foundCliSecureAttribute = true;
					break;
				}
			}
		}

		void findCliSecureRtType() {
			if (cliSecureRtType != null)
				return;

			foreach (var type in module.Types) {
				if (type.Namespace != "")
					continue;
				var typeName = type.FullName;

				MethodDefinition cs = null;
				MethodDefinition initialize = null;
				MethodDefinition postInitialize = null;
				MethodDefinition load = null;

				int methods = 0;
				foreach (var method in type.Methods) {
					if (method.FullName == "System.String " + typeName + "::cs(System.String)") {
						cs = method;
						methods++;
					}
					else if (method.FullName == "System.Void " + typeName + "::Initialize()") {
						initialize = method;
						methods++;
					}
					else if (method.FullName == "System.Void " + typeName + "::PostInitialize()") {
						postInitialize = method;
						methods++;
					}
					else if (method.FullName == "System.IntPtr " + typeName + "::Load()") {
						load = method;
						methods++;
					}
				}
				if (methods < 2)
					continue;

				decryptStringMethod = cs;
				initializeMethod = initialize;
				postInitializeMethod = postInitialize;
				cliSecureRtType = type;
				if (load != null)
					findPossibleNamesToRemove(load);
				return;
			}
		}

		void findStringDecryptBuffer() {
			foreach (var type in module.Types) {
				if (type.FullName == "<D234>" || type.FullName == "<ClassD234>") {
					addTypeToBeRemoved(type, "Obfuscator string decrypter type");
					foreach (var field in type.Fields) {
						if (field.FullName == "<D234> <D234>::345" || field.FullName == "<ClassD234>/D234 <ClassD234>::345") {
							decryptStringBuffer = field.InitialValue;
							break;
						}
					}
					break;
				}
			}
		}

		void findDelegateCreatorType() {
			foreach (var type in module.Types) {
				var methodName = "System.Void " + type.FullName + "::icgd(System.Int32)";
				foreach (var method in type.Methods) {
					if (method.FullName == methodName) {
						proxyDelegateFinder.setDelegateCreatorMethod(method);
						foundProxyDelegateMethod = true;
						return;
					}
				}
			}
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			foreach (var type in module.Types) {
				if (type.FullName == "InitializeDelegate" && DotNetUtils.derivesFromDelegate(type))
					this.addTypeToBeRemoved(type, "Obfuscator type");
				else if (findResourceDecrypter(type)) {
					// Nothing
				}
				if (options.RemoveStackFrameHelper)
					findStackFrameHelper(type);
			}

			proxyDelegateFinder.find();

			if (decryptStringMethod != null)
				staticStringDecrypter.add(decryptStringMethod, (method, args) => decryptString((string)args[0]));

			addCctorInitCallToBeRemoved(initializeMethod);
			addCctorInitCallToBeRemoved(postInitializeMethod);
			if (options.FixResources)
				addCctorInitCallToBeRemoved(rsrcRrrMethod);
		}

		bool findResourceDecrypter(TypeDefinition type) {
			MethodDefinition rrrMethod = null;
			MethodDefinition decryptMethod = null;

			foreach (var method in type.Methods) {
				if (method.Name == "rrr" && DotNetUtils.isMethod(method, "System.Void", "()"))
					rrrMethod = method;
				else if (DotNetUtils.isMethod(method, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)"))
					decryptMethod = method;
			}
			if (rrrMethod == null || decryptMethod == null)
				return false;

			var methodCalls = DotNetUtils.getMethodCallCounts(rrrMethod);
			if (methodCalls.count("System.Void System.ResolveEventHandler::.ctor(System.Object,System.IntPtr)") != 1)
				return false;

			rsrcRrrMethod = rrrMethod;
			rsrcDecryptMethod = decryptMethod;
			return true;
		}

		void decryptResources() {
			if (rsrcDecryptMethod == null)
				return;
			var resource = getResource(DotNetUtils.getCodeStrings(rsrcDecryptMethod)) as EmbeddedResource;
			if (resource == null)
				return;

			DeobUtils.decryptAndAddResources(module, resource.Name, () => decryptResource(resource));

			addResourceToBeRemoved(resource, "Encrypted resource");
			if (rsrcDecryptMethod != null)
				addTypeToBeRemoved(rsrcDecryptMethod.DeclaringType, "Obfuscator resource decrypter type");
		}

		byte[] decryptResource(EmbeddedResource resource) {
			using (var rsrcStream = resource.GetResourceStream()) {
				using (var reader = new BinaryReader(rsrcStream)) {
					var key = reader.ReadString();
					var data = reader.ReadBytes((int)(rsrcStream.Length - rsrcStream.Position));
					var cryptoTransform = new DESCryptoServiceProvider {
						Key = Encoding.ASCII.GetBytes(key),
						IV  = Encoding.ASCII.GetBytes(key),
					}.CreateDecryptor();
					var memStream = new MemoryStream(data);
					using (var reader2 = new BinaryReader(new CryptoStream(memStream, cryptoTransform, CryptoStreamMode.Read))) {
						return reader2.ReadBytes((int)memStream.Length);
					}
				}
			}
		}

		void findStackFrameHelper(TypeDefinition type) {
			if (!type.HasMethods)
				return;
			if (type.Methods.Count > 3)
				return;

			MethodDefinition errorMethod = null;
			foreach (var method in type.Methods) {
				if (method.IsRuntimeSpecialName && method.Name == ".ctor" && method.HasParameters == false)
					continue;	// .ctor is allowed
				if (method.IsRuntimeSpecialName && method.Name == ".cctor" && method.HasParameters == false)
					continue;	// .cctor is allowed
				if (method.IsStatic && method.CallingConvention == MethodCallingConvention.Default &&
					method.ExplicitThis == false && method.HasThis == false &&
					method.HasBody && method.IsManaged && method.IsIL && method.HasParameters &&
					method.Parameters.Count == 2 && method.HasGenericParameters == false &&
					!DotNetUtils.hasReturnValue(method) &&
					MemberReferenceHelper.verifyType(method.Parameters[0].ParameterType, "mscorlib", "System.Exception") &&
					MemberReferenceHelper.verifyType(method.Parameters[1].ParameterType, "mscorlib", "System.Object", "[]")) {
					errorMethod = method;
				}
				else
					return;
			}
			if (errorMethod != null) {
				if (stackFrameHelperType != null)
					throw new ApplicationException("Found another StackFrameHelper");
				stackFrameHelperType = type;
				exceptionLoggerRemover.add(errorMethod);
			}
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			proxyDelegateFinder.deobfuscate(blocks);
			removeStackFrameHelperCode(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			if (options.FixResources)
				decryptResources();
			removeProxyDelegates(proxyDelegateFinder);
			if (exceptionLoggerRemover.NumRemovedExceptionLoggers > 0)
				addTypeToBeRemoved(stackFrameHelperType, "StackFrameHelper type");
			if (Operations.DecryptStrings != OpDecryptString.None)
				addTypeToBeRemoved(cliSecureRtType, "Obfuscator type");
			addResources("Obfuscator protection files");
			addModuleReferences("Obfuscator protection files");

			base.deobfuscateEnd();
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			if (decryptStringMethod != null)
				list.Add(decryptStringMethod.MetadataToken.ToInt32().ToString("X8"));
			return list;
		}

		string decryptString(string es) {
			if (decryptStringBuffer == null)
				throw new ApplicationException("Trying to decrypt strings when decryptStringBuffer is null (could not find it!)");
			StringBuilder sb = new StringBuilder(es.Length);
			for (int i = 0; i < es.Length; i++)
				sb.Append(Convert.ToChar((int)(es[i] ^ decryptStringBuffer[i % decryptStringBuffer.Length])));
			return sb.ToString();
		}

		void removeStackFrameHelperCode(Blocks blocks) {
			if (exceptionLoggerRemover.remove(blocks))
				Log.v("Removed StackFrameHelper code");
		}
	}
}
