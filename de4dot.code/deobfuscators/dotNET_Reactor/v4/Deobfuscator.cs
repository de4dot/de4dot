/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.MyStuff;
using de4dot.blocks;
using de4dot.PE;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = ".NET Reactor";
		public const string THE_TYPE = "dr4";
		const string DEFAULT_REGEX = DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;
		BoolOption decryptMethods;
		BoolOption decryptBools;
		BoolOption restoreTypes;
		BoolOption inlineMethods;
		BoolOption removeInlinedMethods;
		BoolOption dumpEmbeddedAssemblies;
		BoolOption decryptResources;
		BoolOption removeNamespaces;
		BoolOption removeAntiStrongName;
		NoArgOption dumpNativeMethods;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			decryptMethods = new BoolOption(null, makeArgName("methods"), "Decrypt methods", true);
			decryptBools = new BoolOption(null, makeArgName("bools"), "Decrypt booleans", true);
			restoreTypes = new BoolOption(null, makeArgName("types"), "Restore types (object -> real type)", true);
			inlineMethods = new BoolOption(null, makeArgName("inline"), "Inline short methods", true);
			removeInlinedMethods = new BoolOption(null, makeArgName("remove-inlined"), "Remove inlined methods", true);
			dumpEmbeddedAssemblies = new BoolOption(null, makeArgName("embedded"), "Dump embedded assemblies", true);
			decryptResources = new BoolOption(null, makeArgName("rsrc"), "Decrypt resources", true);
			removeNamespaces = new BoolOption(null, makeArgName("ns1"), "Clear namespace if there's only one class in it", true);
			removeAntiStrongName = new BoolOption(null, makeArgName("sn"), "Remove anti strong name code", true);
			dumpNativeMethods = new NoArgOption(null, makeArgName("dump-native"), "Dump native methods to filename.dll.native");
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
				DecryptMethods = decryptMethods.get(),
				DecryptBools = decryptBools.get(),
				RestoreTypes = restoreTypes.get(),
				InlineMethods = inlineMethods.get(),
				RemoveInlinedMethods = removeInlinedMethods.get(),
				DumpEmbeddedAssemblies = dumpEmbeddedAssemblies.get(),
				DecryptResources = decryptResources.get(),
				RemoveNamespaces = removeNamespaces.get(),
				RemoveAntiStrongName = removeAntiStrongName.get(),
				DumpNativeMethods = dumpNativeMethods.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				decryptMethods,
				decryptBools,
				restoreTypes,
				inlineMethods,
				removeInlinedMethods,
				dumpEmbeddedAssemblies,
				decryptResources,
				removeNamespaces,
				removeAntiStrongName,
				dumpNativeMethods,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;

		PeImage peImage;
		byte[] fileData;
		MethodsDecrypter methodsDecrypter;
		StringDecrypter stringDecrypter;
		BooleanDecrypter booleanDecrypter;
		BooleanValueInliner booleanValueInliner;
		MetadataTokenObfuscator metadataTokenObfuscator;
		AssemblyResolver assemblyResolver;
		ResourceResolver resourceResolver;
		AntiStrongName antiStrongname;
		EmptyClass emptyClass;
		ProxyCallFixer proxyCallFixer;

		bool unpackedNativeFile = false;
		bool canRemoveDecrypterType = true;
		bool startedDeobfuscating = false;

		internal class Options : OptionsBase {
			public bool DecryptMethods { get; set; }
			public bool DecryptBools { get; set; }
			public bool RestoreTypes { get; set; }
			public bool InlineMethods { get; set; }
			public bool RemoveInlinedMethods { get; set; }
			public bool DumpEmbeddedAssemblies { get; set; }
			public bool DecryptResources { get; set; }
			public bool RemoveNamespaces { get; set; }
			public bool RemoveAntiStrongName { get; set; }
			public bool DumpNativeMethods { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME + " 4.x"; }
		}

		public override string Name {
			get { return obfuscatorName; }
		}

		protected override bool CanInlineMethods {
			get { return startedDeobfuscating ? options.InlineMethods : true; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;

			if (options.RemoveNamespaces)
				this.RenamingOptions |= RenamingOptions.RemoveNamespaceIfOneType;
			else
				this.RenamingOptions &= ~RenamingOptions.RemoveNamespaceIfOneType;
		}

		public override byte[] unpackNativeFile(PeImage peImage) {
			var data = new NativeImageUnpacker(peImage).unpack();
			if (data == null)
				return null;

			unpackedNativeFile = true;
			ModuleBytes = data;
			return data;
		}

		public override void init(ModuleDefinition module) {
			base.init(module);
		}

		static Regex isRandomName = new Regex(@"^[A-Z]{30,40}$");
		static Regex isRandomNameMembers = new Regex(@"^[a-zA-Z0-9]{9,11}$");	// methods, fields, props, events
		static Regex isRandomNameTypes = new Regex(@"^[a-zA-Z0-9]{18,19}(?:`\d+)?$");	// types, namespaces

		bool checkValidName(string name, Regex regex) {
			if (isRandomName.IsMatch(name))
				return false;
			if (regex.IsMatch(name)) {
				if (RandomNameChecker.isRandom(name))
					return false;
				if (!RandomNameChecker.isNonRandom(name))
					return false;
			}
			return checkValidName(name);
		}

		public override bool isValidNamespaceName(string ns) {
			if (ns == null)
				return false;
			if (ns.Contains("."))
				return base.isValidNamespaceName(ns);
			return checkValidName(ns, isRandomNameTypes);
		}

		public override bool isValidTypeName(string name) {
			return name != null && checkValidName(name, isRandomNameTypes);
		}

		public override bool isValidMethodName(string name) {
			return name != null && checkValidName(name, isRandomNameMembers);
		}

		public override bool isValidPropertyName(string name) {
			return name != null && checkValidName(name, isRandomNameMembers);
		}

		public override bool isValidEventName(string name) {
			return name != null && checkValidName(name, isRandomNameMembers);
		}

		public override bool isValidFieldName(string name) {
			return name != null && checkValidName(name, isRandomNameMembers);
		}

		public override bool isValidGenericParamName(string name) {
			return name != null && checkValidName(name, isRandomNameMembers);
		}

		public override bool isValidMethodArgName(string name) {
			return name != null && checkValidName(name, isRandomNameMembers);
		}

		public override bool isValidResourceKeyName(string name) {
			return name != null && checkValidName(name, isRandomNameMembers);
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(methodsDecrypter.Detected) +
					toInt32(stringDecrypter.Detected) +
					toInt32(booleanDecrypter.Detected) +
					toInt32(assemblyResolver.Detected) +
					toInt32(resourceResolver.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			if (sum == 0) {
				if (hasMetadataStream("#GUlD") && hasMetadataStream("#Blop"))
					val += 10;
			}

			return val;
		}

		protected override void scanForObfuscator() {
			methodsDecrypter = new MethodsDecrypter(module);
			methodsDecrypter.find();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find(DeobfuscatedFile);
			booleanDecrypter = new BooleanDecrypter(module);
			booleanDecrypter.find();
			assemblyResolver = new AssemblyResolver(module);
			assemblyResolver.find(DeobfuscatedFile);
			obfuscatorName = detectVersion();
			if (unpackedNativeFile)
				obfuscatorName += " (native)";
			resourceResolver = new ResourceResolver(module);
			resourceResolver.find(DeobfuscatedFile);
		}

		string detectVersion() {
			/*
			Methods decrypter locals (not showing its own types):
			3.7.0.3:
					"System.Byte[]"
					"System.Int32"
					"System.Int32[]"
					"System.IntPtr"
					"System.IO.BinaryReader"
					"System.IO.MemoryStream"
					"System.Object"
					"System.Reflection.Assembly"
					"System.Security.Cryptography.CryptoStream"
					"System.Security.Cryptography.ICryptoTransform"
					"System.Security.Cryptography.RijndaelManaged"
					"System.String"

			3.9.8.0:
			-		"System.Int32[]"
			+		"System.Diagnostics.StackFrame"

			4.0.0.0: (jitter)
			-		"System.Diagnostics.StackFrame"
			-		"System.Object"
			+		"System.Boolean"
			+		"System.Collections.IEnumerator"
			+		"System.Delegate"
			+		"System.Diagnostics.Process"
			+		"System.Diagnostics.ProcessModule"
			+		"System.Diagnostics.ProcessModuleCollection"
			+		"System.IDisposable"
			+		"System.Int64"
			+		"System.UInt32"
			+		"System.UInt64"

			4.1.0.0: (jitter)
			+		"System.Reflection.Assembly"

			4.3.1.0: (jitter)
			+		"System.Byte&"
			*/

			LocalTypes localTypes;
			int minVer = -1;
			foreach (var info in stringDecrypter.DecrypterInfos) {
				if (info.key == null)
					continue;
				localTypes = new LocalTypes(info.method);
				if (!localTypes.exists("System.IntPtr"))
					return DeobfuscatorInfo.THE_NAME + " <= 3.7";
				minVer = 3800;
				break;
			}

			if (methodsDecrypter.Method == null) {
				if (minVer >= 3800)
					return DeobfuscatorInfo.THE_NAME + " >= 3.8";
				return DeobfuscatorInfo.THE_NAME;
			}
			localTypes = new LocalTypes(methodsDecrypter.Method);

			if (localTypes.exists("System.Int32[]")) {
				if (minVer >= 3800)
					return DeobfuscatorInfo.THE_NAME + " 3.8.4.1 - 3.9.0.1";
				return DeobfuscatorInfo.THE_NAME + " <= 3.9.0.1";
			}
			if (!localTypes.exists("System.Diagnostics.Process")) {	// If < 4.0
				if (localTypes.exists("System.Diagnostics.StackFrame"))
					return DeobfuscatorInfo.THE_NAME + " 3.9.8.0";
			}

			var compileMethod = MethodsDecrypter.findDnrCompileMethod(methodsDecrypter.Method.DeclaringType);
			if (compileMethod == null)
				return DeobfuscatorInfo.THE_NAME + " < 4.0";
			DeobfuscatedFile.deobfuscate(compileMethod);
			bool compileMethodHasConstant_0x70000000 = DeobUtils.hasInteger(compileMethod, 0x70000000);	// 4.0-4.1
			DeobfuscatedFile.deobfuscate(methodsDecrypter.Method);
			bool hasCorEnableProfilingString = findString(methodsDecrypter.Method, "Cor_Enable_Profiling");	// 4.1-4.4

			if (compileMethodHasConstant_0x70000000) {
				if (hasCorEnableProfilingString)
					return DeobfuscatorInfo.THE_NAME + " 4.1";
				return DeobfuscatorInfo.THE_NAME + " 4.0";
			}
			if (!hasCorEnableProfilingString)
				return DeobfuscatorInfo.THE_NAME;
			// 4.2-4.4

			if (!localTypes.exists("System.Byte&"))
				return DeobfuscatorInfo.THE_NAME + " 4.2";

			localTypes = new LocalTypes(compileMethod);
			if (localTypes.exists("System.Object"))
				return DeobfuscatorInfo.THE_NAME + " 4.4";
			return DeobfuscatorInfo.THE_NAME + " 4.3";
		}

		static bool findString(MethodDefinition method, string s) {
			foreach (var cs in DotNetUtils.getCodeStrings(method)) {
				if (cs == s)
					return true;
			}
			return false;
		}

		public override bool getDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (count != 0)
				return false;
			fileData = ModuleBytes ?? DeobUtils.readModule(module);
			peImage = new PeImage(fileData);

			if (!options.DecryptMethods)
				return false;

			var tokenToNativeCode = new Dictionary<uint,byte[]>();
			if (!methodsDecrypter.decrypt(peImage, DeobfuscatedFile, ref dumpedMethods, tokenToNativeCode))
				return false;

			if (options.DumpNativeMethods) {
				using (var fileStream = new FileStream(module.FullyQualifiedName + ".native", FileMode.Create, FileAccess.Write, FileShare.Read)) {
					var sortedTokens = new List<uint>(tokenToNativeCode.Keys);
					sortedTokens.Sort();
					var writer = new BinaryWriter(fileStream);
					var nops = new byte[] { 0x90, 0x90, 0x90, 0x90 };
					foreach (var token in sortedTokens) {
						writer.Write((byte)0xB8);
						writer.Write(token);
						writer.Write(tokenToNativeCode[token]);
						writer.Write(nops);
					}
				}
			}

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			newOne.setModule(module);
			newOne.fileData = fileData;
			newOne.peImage = new PeImage(fileData);
			newOne.methodsDecrypter = new MethodsDecrypter(module, methodsDecrypter);
			newOne.stringDecrypter = new StringDecrypter(module, stringDecrypter);
			newOne.booleanDecrypter = new BooleanDecrypter(module, booleanDecrypter);
			newOne.assemblyResolver = new AssemblyResolver(module, assemblyResolver);
			newOne.resourceResolver = new ResourceResolver(module, resourceResolver);
			newOne.methodsDecrypter.reloaded();
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			proxyCallFixer = new ProxyCallFixer(module, DeobfuscatedFile);
			proxyCallFixer.findDelegateCreator();
			proxyCallFixer.find();

			stringDecrypter.init(peImage, fileData, DeobfuscatedFile);
			booleanDecrypter.init(fileData, DeobfuscatedFile);
			booleanValueInliner = new BooleanValueInliner();
			emptyClass = new EmptyClass(module);

			if (options.DecryptBools) {
				booleanValueInliner.add(booleanDecrypter.Method, (method, gim, args) => {
					return booleanDecrypter.decrypt((int)args[0]);
				});
			}

			foreach (var info in stringDecrypter.DecrypterInfos) {
				staticStringInliner.add(info.method, (method2, gim, args) => {
					return stringDecrypter.decrypt(method2, (int)args[0]);
				});
			}
			if (stringDecrypter.OtherStringDecrypter != null) {
				staticStringInliner.add(stringDecrypter.OtherStringDecrypter, (method2, gim, args) => {
					return stringDecrypter.decrypt((string)args[0]);
				});
			}
			DeobfuscatedFile.stringDecryptersAdded();

			metadataTokenObfuscator = new MetadataTokenObfuscator(module);
			antiStrongname = new AntiStrongName(getDecrypterType());

			bool removeResourceResolver = false;
			if (options.DecryptResources) {
				resourceResolver.init(DeobfuscatedFile, this);
				decryptResources();
				if (options.InlineMethods) {
					addTypeToBeRemoved(resourceResolver.Type, "Resource decrypter type");
					removeResourceResolver = true;
				}
				addEntryPointCallToBeRemoved(resourceResolver.InitMethod);
				addCctorInitCallToBeRemoved(resourceResolver.InitMethod);
			}
			if (resourceResolver.Detected && !removeResourceResolver && !resourceResolver.FoundResource)
				canRemoveDecrypterType = false;	// There may be calls to its .ctor

			if (Operations.DecryptStrings != OpDecryptString.None)
				addResourceToBeRemoved(stringDecrypter.Resource, "Encrypted strings");
			else
				canRemoveDecrypterType = false;

			if (options.DecryptMethods && !methodsDecrypter.HasNativeMethods) {
				addResourceToBeRemoved(methodsDecrypter.Resource, "Encrypted methods");
				addCctorInitCallToBeRemoved(methodsDecrypter.Method);
			}
			else
				canRemoveDecrypterType = false;

			if (options.DecryptBools)
				addResourceToBeRemoved(booleanDecrypter.Resource, "Encrypted booleans");
			else
				canRemoveDecrypterType = false;

			if (!options.RemoveAntiStrongName)
				canRemoveDecrypterType = false;

			// The inlined methods may contain calls to the decrypter class
			if (!options.InlineMethods)
				canRemoveDecrypterType = false;

			if (options.DumpEmbeddedAssemblies) {
				if (options.InlineMethods)
					addTypeToBeRemoved(assemblyResolver.Type, "Assembly resolver");
				addEntryPointCallToBeRemoved(assemblyResolver.InitMethod);
				addCctorInitCallToBeRemoved(assemblyResolver.InitMethod);
				dumpEmbeddedAssemblies();
			}

			if (options.InlineMethods)
				addTypeToBeRemoved(metadataTokenObfuscator.Type, "Metadata token obfuscator");

			addCctorInitCallToBeRemoved(emptyClass.Method);
			addCtorInitCallToBeRemoved(emptyClass.Method);
			addEntryPointCallToBeRemoved(emptyClass.Method);
			if (options.InlineMethods)
				addTypeToBeRemoved(emptyClass.Type, "Empty class");

			startedDeobfuscating = true;
		}

		void addEntryPointCallToBeRemoved(MethodDefinition methodToBeRemoved) {
			var entryPoint = module.EntryPoint;
			addCallToBeRemoved(entryPoint, methodToBeRemoved);
			foreach (var calledMethod in DotNetUtils.getCalledMethods(module, entryPoint))
				addCallToBeRemoved(calledMethod, methodToBeRemoved);
		}

		void decryptResources() {
			var rsrc = resourceResolver.mergeResources();
			if (rsrc == null)
				return;
			addResourceToBeRemoved(rsrc, "Encrypted resources");
		}

		void dumpEmbeddedAssemblies() {
			if (!options.DumpEmbeddedAssemblies)
				return;
			foreach (var info in assemblyResolver.getEmbeddedAssemblies(DeobfuscatedFile, this)) {
				var simpleName = Utils.getAssemblySimpleName(info.name);
				DeobfuscatedFile.createAssemblyFile(info.resource.GetResourceData(), simpleName, null);
				addResourceToBeRemoved(info.resource, string.Format("Embedded assembly: {0}", info.name));
			}
		}

		public override bool deobfuscateOther(Blocks blocks) {
			return booleanValueInliner.decrypt(blocks) > 0;
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			proxyCallFixer.deobfuscate(blocks);
			metadataTokenObfuscator.deobfuscate(blocks);
			fixTypeofDecrypterInstructions(blocks);
			removeAntiStrongNameCode(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		void removeAntiStrongNameCode(Blocks blocks) {
			if (!options.RemoveAntiStrongName)
				return;
			if (antiStrongname.remove(blocks))
				Log.v("Removed anti strong name code");
		}

		TypeDefinition getDecrypterType() {
			return methodsDecrypter.DecrypterType ?? stringDecrypter.DecrypterType ?? booleanDecrypter.DecrypterType;
		}

		void fixTypeofDecrypterInstructions(Blocks blocks) {
			var type = getDecrypterType();
			if (type == null)
				return;

			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instructions = block.Instructions;
				for (int i = 0; i < instructions.Count; i++) {
					var instr = instructions[i];
					if (instr.OpCode.Code != Code.Ldtoken)
						continue;
					if (!MemberReferenceHelper.compareTypes(type, instr.Operand as TypeReference))
						continue;
					instructions[i] = new Instr(Instruction.Create(OpCodes.Ldtoken, blocks.Method.DeclaringType));
				}
			}
		}

		public override void deobfuscateEnd() {
			removeProxyDelegates(proxyCallFixer);
			removeInlinedMethods();
			if (options.RestoreTypes)
				new TypesRestorer(module).deobfuscate();

			var decrypterType = getDecrypterType();
			if (canRemoveDecrypterType && isTypeCalled(decrypterType))
				canRemoveDecrypterType = false;

			if (canRemoveDecrypterType)
				addTypeToBeRemoved(decrypterType, "Decrypter type");
			else
				Log.v("Could not remove decrypter type");

			base.deobfuscateEnd();
		}

		void removeInlinedMethods() {
			if (!options.InlineMethods || !options.RemoveInlinedMethods)
				return;
			findAndRemoveInlinedMethods();
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			foreach (var info in stringDecrypter.DecrypterInfos)
				list.Add(info.method.MetadataToken.ToInt32());
			if (stringDecrypter.OtherStringDecrypter != null)
				list.Add(stringDecrypter.OtherStringDecrypter.MetadataToken.ToInt32());
			return list;
		}

		public override void OnBeforeAddingResources(MetadataBuilder builder) {
			if (!options.DecryptMethods)
				return;
			methodsDecrypter.encryptNativeMethods(builder);
		}
	}
}
