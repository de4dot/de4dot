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
using Mono.Cecil;
using Mono.MyStuff;
using de4dot.blocks;
using de4dot.PE;

namespace de4dot.code.deobfuscators.CliSecure {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "CliSecure";
		public const string THE_TYPE = "cs";
		const string DEFAULT_REGEX = @"[a-zA-Z_0-9>}$]$";
		BoolOption decryptMethods;
		BoolOption decryptResources;
		BoolOption removeStackFrameHelper;
		BoolOption restoreVmCode;
		BoolOption setInitLocals;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			decryptMethods = new BoolOption(null, makeArgName("methods"), "Decrypt methods", true);
			decryptResources = new BoolOption(null, makeArgName("rsrc"), "Decrypt resources", true);
			removeStackFrameHelper = new BoolOption(null, makeArgName("stack"), "Remove all StackFrameHelper code", true);
			restoreVmCode = new BoolOption(null, makeArgName("vm"), "Restore VM code", true);
			setInitLocals = new BoolOption(null, makeArgName("initlocals"), "Set initlocals in method header", true);
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
				DecryptResources = decryptResources.get(),
				RemoveStackFrameHelper = removeStackFrameHelper.get(),
				RestoreVmCode = restoreVmCode.get(),
				SetInitLocals = setInitLocals.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				decryptMethods,
				decryptResources,
				removeStackFrameHelper,
				restoreVmCode,
				setInitLocals,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;

		List<TypeDefinition> cliSecureAttributes = new List<TypeDefinition>();
		ProxyCallFixer proxyCallFixer;
		CliSecureRtType cliSecureRtType;
		StringDecrypter stringDecrypter;
		ResourceDecrypter resourceDecrypter;

		StackFrameHelper stackFrameHelper;
		vm.Csvm csvm;

		internal class Options : OptionsBase {
			public bool DecryptMethods { get; set; }
			public bool DecryptResources { get; set; }
			public bool RemoveStackFrameHelper { get; set; }
			public bool RestoreVmCode { get; set; }
			public bool SetInitLocals { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return obfuscatorName; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		public override void init(ModuleDefinition module) {
			base.init(module);
		}

		public override byte[] unpackNativeFile(PeImage peImage) {
			return unpackNativeFile1(peImage) ?? unpackNativeFile2(peImage);
		}

		// Old CS versions
		byte[] unpackNativeFile1(PeImage peImage) {
			const int dataDirNum = 6;	// debug dir
			const int dotNetDirNum = 14;

			if (peImage.OptionalHeader.dataDirectories[dataDirNum].virtualAddress == 0)
				return null;
			if (peImage.OptionalHeader.dataDirectories[dataDirNum].size != 0x48)
				return null;

			var fileData = peImage.readAllBytes();
			int dataDir = (int)peImage.OptionalHeader.offsetOfDataDirectory(dataDirNum);
			int dotNetDir = (int)peImage.OptionalHeader.offsetOfDataDirectory(dotNetDirNum);
			writeUInt32(fileData, dotNetDir, BitConverter.ToUInt32(fileData, dataDir));
			writeUInt32(fileData, dotNetDir + 4, BitConverter.ToUInt32(fileData, dataDir + 4));
			writeUInt32(fileData, dataDir, 0);
			writeUInt32(fileData, dataDir + 4, 0);
			ModuleBytes = fileData;
			return fileData;
		}

		// CS 1.x
		byte[] unpackNativeFile2(PeImage peImage) {
			var dir = peImage.Resources.getRoot();
			if ((dir = dir.getDirectory("ASSEMBLY")) == null)
				return null;
			if ((dir = dir.getDirectory(101)) == null)
				return null;
			var data = dir.getData(0);
			if (data == null)
				return null;

			return ModuleBytes = peImage.readBytes(data.RVA, (int)data.Size);
		}

		static void writeUInt32(byte[] data, int offset, uint value) {
			data[offset] = (byte)value;
			data[offset + 1] = (byte)(value >> 8);
			data[offset + 2] = (byte)(value >> 16);
			data[offset + 3] = (byte)(value >> 24);
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(cliSecureRtType.Detected) +
					toInt32(stringDecrypter.Detected) +
					toInt32(proxyCallFixer.Detected) +
					toInt32(resourceDecrypter.Detected) +
					toInt32(csvm.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);
			if (cliSecureAttributes.Count != 0)
				val += 10;

			return val;
		}

		protected override void scanForObfuscator() {
			findCliSecureAttribute();
			cliSecureRtType = new CliSecureRtType(module);
			cliSecureRtType.find(ModuleBytes);
			stringDecrypter = new StringDecrypter(module, cliSecureRtType.StringDecrypterMethod);
			stringDecrypter.find();
			resourceDecrypter = new ResourceDecrypter(module);
			resourceDecrypter.find();
			proxyCallFixer = new ProxyCallFixer(module);
			proxyCallFixer.findDelegateCreator();
			csvm = new vm.Csvm(DeobfuscatedFile.DeobfuscatorContext, module);
			csvm.find();
		}

		void findCliSecureAttribute() {
			foreach (var type in module.Types) {
				if (Utils.StartsWith(type.FullName, "SecureTeam.Attributes.ObfuscatedByCliSecureAttribute", StringComparison.Ordinal)) {
					cliSecureAttributes.Add(type);
					obfuscatorName = "CliSecure";
				}
				else if (Utils.StartsWith(type.FullName, "SecureTeam.Attributes.ObfuscatedByAgileDotNetAttribute", StringComparison.Ordinal)) {
					cliSecureAttributes.Add(type);
					obfuscatorName = "Agile.NET";
				}
			}
		}

		public override bool getDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (count != 0 || !options.DecryptMethods)
				return false;

			byte[] fileData = ModuleBytes ?? DeobUtils.readModule(module);
			var peImage = new PeImage(fileData);

			if (!new MethodsDecrypter().decrypt(peImage, module, cliSecureRtType, ref dumpedMethods)) {
				Log.v("Methods aren't encrypted or invalid signature");
				return false;
			}

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			newOne.setModule(module);
			newOne.cliSecureAttributes = lookup(module, cliSecureAttributes, "Could not find CliSecure attribute");
			newOne.cliSecureRtType = new CliSecureRtType(module, cliSecureRtType);
			newOne.stringDecrypter = new StringDecrypter(module, stringDecrypter);
			newOne.resourceDecrypter = new ResourceDecrypter(module, resourceDecrypter);
			newOne.proxyCallFixer = new ProxyCallFixer(module, proxyCallFixer);
			newOne.csvm = new vm.Csvm(DeobfuscatedFile.DeobfuscatorContext, module, csvm);
			return newOne;
		}

		static List<TypeDefinition> lookup(ModuleDefinition module, List<TypeDefinition> types, string errorMsg) {
			var list = new List<TypeDefinition>(types.Count);
			foreach (var type in types)
				list.Add(DeobUtils.lookup(module, type, errorMsg));
			return list;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			cliSecureRtType.findStringDecrypterMethod();
			stringDecrypter.Method = cliSecureRtType.StringDecrypterMethod;

			addAttributesToBeRemoved(cliSecureAttributes, "Obfuscator attribute");

			if (options.DecryptResources) {
				decryptResources(resourceDecrypter);
				addCctorInitCallToBeRemoved(resourceDecrypter.RsrcRrrMethod);
			}

			stackFrameHelper = new StackFrameHelper(module);
			stackFrameHelper.find();

			foreach (var type in module.Types) {
				if (type.FullName == "InitializeDelegate" && DotNetUtils.derivesFromDelegate(type))
					this.addTypeToBeRemoved(type, "Obfuscator type");
			}

			proxyCallFixer.find();

			staticStringInliner.add(stringDecrypter.Method, (method, gim, args) => stringDecrypter.decrypt((string)args[0]));
			DeobfuscatedFile.stringDecryptersAdded();

			if (options.DecryptMethods) {
				addCctorInitCallToBeRemoved(cliSecureRtType.InitializeMethod);
				addCctorInitCallToBeRemoved(cliSecureRtType.PostInitializeMethod);
				findPossibleNamesToRemove(cliSecureRtType.LoadMethod);
			}

			if (options.RestoreVmCode) {
				csvm.restore();
				addAssemblyReferenceToBeRemoved(csvm.VmAssemblyReference, "CSVM assembly reference");
				addResourceToBeRemoved(csvm.Resource, "CSVM data resource");
			}
		}

		void decryptResources(ResourceDecrypter resourceDecrypter) {
			var rsrc = resourceDecrypter.mergeResources();
			if (rsrc == null)
				return;
			addResourceToBeRemoved(rsrc, "Encrypted resources");
			addTypeToBeRemoved(resourceDecrypter.Type, "Resource decrypter type");
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			proxyCallFixer.deobfuscate(blocks);
			removeStackFrameHelperCode(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			if (options.SetInitLocals)
				setInitLocals();
			removeProxyDelegates(proxyCallFixer);
			if (options.RemoveStackFrameHelper) {
				if (stackFrameHelper.ExceptionLoggerRemover.NumRemovedExceptionLoggers > 0)
					addTypeToBeRemoved(stackFrameHelper.Type, "StackFrameHelper type");
			}
			if (CanRemoveStringDecrypterType) {
				addTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");
				if (options.DecryptMethods)
					addTypeToBeRemoved(cliSecureRtType.Type, "Obfuscator type");
			}
			if (options.DecryptMethods) {
				addResources("Obfuscator protection files");
				addModuleReferencesToBeRemoved(cliSecureRtType.DecryptModuleReferences, "Obfuscator protection files");
				addModuleReferences("Obfuscator protection files");
			}

			module.Attributes |= ModuleAttributes.ILOnly;

			base.deobfuscateEnd();
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter.Method != null)
				list.Add(stringDecrypter.Method.MetadataToken.ToInt32());
			return list;
		}

		void removeStackFrameHelperCode(Blocks blocks) {
			if (!options.RemoveStackFrameHelper)
				return;
			if (stackFrameHelper.ExceptionLoggerRemover.remove(blocks))
				Log.v("Removed StackFrameHelper code");
		}
	}
}
