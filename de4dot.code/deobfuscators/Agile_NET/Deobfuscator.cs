/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using dnlib.IO;
using dnlib.PE;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Agile.NET";
		public const string THE_TYPE = "an";
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

		List<TypeDef> cliSecureAttributes = new List<TypeDef>();
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

		public override void init(ModuleDefMD module) {
			base.init(module);
		}

		public override byte[] unpackNativeFile(IPEImage peImage) {
			return unpackNativeFile1(peImage) ?? unpackNativeFile2(peImage);
		}

		// Old CS versions
		byte[] unpackNativeFile1(IPEImage peImage) {
			const int dataDirNum = 6;	// debug dir
			const int dotNetDirNum = 14;

			var optHeader = peImage.ImageNTHeaders.OptionalHeader;
			if (optHeader.DataDirectories[dataDirNum].VirtualAddress == 0)
				return null;
			if (optHeader.DataDirectories[dataDirNum].Size != 0x48)
				return null;

			var fileData = peImage.GetImageAsByteArray();
			long dataDirBaseOffset = (long)optHeader.DataDirectories[0].StartOffset;
			int dataDir = (int)dataDirBaseOffset + dataDirNum * 8;
			int dotNetDir = (int)dataDirBaseOffset + dotNetDirNum * 8;
			writeUInt32(fileData, dotNetDir, BitConverter.ToUInt32(fileData, dataDir));
			writeUInt32(fileData, dotNetDir + 4, BitConverter.ToUInt32(fileData, dataDir + 4));
			writeUInt32(fileData, dataDir, 0);
			writeUInt32(fileData, dataDir + 4, 0);
			ModuleBytes = fileData;
			return fileData;
		}

		// CS 1.x
		byte[] unpackNativeFile2(IPEImage peImage) {
			var data = peImage.FindWin32ResourceData("ASSEMBLY", 101, 0);
			if (data == null)
				return null;

			return ModuleBytes = data.Data.ReadAllBytes();
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
			obfuscatorName = "CliSecure";
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
			using (var peImage = new MyPEImage(fileData)) {
				if (!new MethodsDecrypter().decrypt(peImage, module, cliSecureRtType, ref dumpedMethods)) {
					Logger.v("Methods aren't encrypted or invalid signature");
					return false;
				}
			}

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefMD module) {
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

		static List<TypeDef> lookup(ModuleDefMD module, List<TypeDef> types, string errorMsg) {
			var list = new List<TypeDef>(types.Count);
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
				if (csvm.restore())
					addResourceToBeRemoved(csvm.Resource, "CSVM data resource");
				else {
					Logger.e("Couldn't restore VM methods. Use --dont-rename or it will not run");
					preserveTokensAndTypes();
				}
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
			}

			base.deobfuscateEnd();

			// Call hasNativeMethods() after all types/methods/etc have been removed since
			// some of the removed methods could be native methods
			if (!module.IsILOnly && !hasNativeMethods())
				module.IsILOnly = true;
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter.Method != null)
				list.Add(stringDecrypter.Method.MDToken.ToInt32());
			return list;
		}

		void removeStackFrameHelperCode(Blocks blocks) {
			if (!options.RemoveStackFrameHelper)
				return;
			if (stackFrameHelper.ExceptionLoggerRemover.remove(blocks))
				Logger.v("Removed StackFrameHelper code");
		}
	}
}
