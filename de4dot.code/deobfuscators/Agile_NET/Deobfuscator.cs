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
using dnlib.IO;
using dnlib.PE;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Agile.NET";
		public const string THE_TYPE = "an";
		const string DEFAULT_REGEX = DeobfuscatorBase.DEFAULT_ASIAN_VALID_NAME_REGEX;
		BoolOption decryptMethods;
		BoolOption decryptResources;
		BoolOption removeStackFrameHelper;
		BoolOption restoreVmCode;
		BoolOption setInitLocals;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			decryptMethods = new BoolOption(null, MakeArgName("methods"), "Decrypt methods", true);
			decryptResources = new BoolOption(null, MakeArgName("rsrc"), "Decrypt resources", true);
			removeStackFrameHelper = new BoolOption(null, MakeArgName("stack"), "Remove all StackFrameHelper code", true);
			restoreVmCode = new BoolOption(null, MakeArgName("vm"), "Restore VM code", true);
			setInitLocals = new BoolOption(null, MakeArgName("initlocals"), "Set initlocals in method header", true);
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator CreateDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.Get(),
				DecryptMethods = decryptMethods.Get(),
				DecryptResources = decryptResources.Get(),
				RemoveStackFrameHelper = removeStackFrameHelper.Get(),
				RestoreVmCode = restoreVmCode.Get(),
				SetInitLocals = setInitLocals.Get(),
			});
		}

		protected override IEnumerable<Option> GetOptionsInternal() {
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
		vm.v1.Csvm csvmV1;
		vm.v2.Csvm csvmV2;

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

		public override void Initialize(ModuleDefMD module) {
			base.Initialize(module);
		}

		public override byte[] UnpackNativeFile(IPEImage peImage) {
			return UnpackNativeFile1(peImage) ?? UnpackNativeFile2(peImage);
		}

		// Old CS versions
		byte[] UnpackNativeFile1(IPEImage peImage) {
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
			WriteUInt32(fileData, dotNetDir, BitConverter.ToUInt32(fileData, dataDir));
			WriteUInt32(fileData, dotNetDir + 4, BitConverter.ToUInt32(fileData, dataDir + 4));
			WriteUInt32(fileData, dataDir, 0);
			WriteUInt32(fileData, dataDir + 4, 0);
			ModuleBytes = fileData;
			return fileData;
		}

		// CS 1.x
		byte[] UnpackNativeFile2(IPEImage peImage) {
			var data = peImage.FindWin32ResourceData("ASSEMBLY", 101, 0);
			if (data == null)
				return null;

			return ModuleBytes = data.Data.ReadAllBytes();
		}

		static void WriteUInt32(byte[] data, int offset, uint value) {
			data[offset] = (byte)value;
			data[offset + 1] = (byte)(value >> 8);
			data[offset + 2] = (byte)(value >> 16);
			data[offset + 3] = (byte)(value >> 24);
		}

		protected override int DetectInternal() {
			int val = 0;

			int sum = ToInt32(cliSecureRtType.Detected) +
					ToInt32(stringDecrypter.Detected) +
					ToInt32(proxyCallFixer.Detected) +
					ToInt32(resourceDecrypter.Detected) +
					ToInt32(csvmV1.Detected || csvmV2.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);
			if (cliSecureAttributes.Count != 0)
				val += 10;

			return val;
		}

		protected override void ScanForObfuscator() {
			FindCliSecureAttribute();
			cliSecureRtType = new CliSecureRtType(module);
			cliSecureRtType.Find(ModuleBytes);
			stringDecrypter = new StringDecrypter(module, cliSecureRtType.StringDecrypterInfos);
			stringDecrypter.Find();
			resourceDecrypter = new ResourceDecrypter(module);
			resourceDecrypter.Find();
			proxyCallFixer = new ProxyCallFixer(module);
			proxyCallFixer.FindDelegateCreator();
			csvmV1 = new vm.v1.Csvm(DeobfuscatedFile.DeobfuscatorContext, module);
			csvmV1.Find();
			csvmV2 = new vm.v2.Csvm(DeobfuscatedFile.DeobfuscatorContext, module);
			csvmV2.Find();
		}

		void FindCliSecureAttribute() {
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

		public override bool GetDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (count != 0 || !options.DecryptMethods)
				return false;

			byte[] fileData = ModuleBytes ?? DeobUtils.ReadModule(module);
			using (var peImage = new MyPEImage(fileData)) {
				if (!new MethodsDecrypter().Decrypt(peImage, module, cliSecureRtType, ref dumpedMethods)) {
					Logger.v("Methods aren't encrypted or invalid signature");
					return false;
				}
			}

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator ModuleReloaded(ModuleDefMD module) {
			var newOne = new Deobfuscator(options);
			newOne.SetModule(module);
			newOne.cliSecureAttributes = Lookup(module, cliSecureAttributes, "Could not find CliSecure attribute");
			newOne.cliSecureRtType = new CliSecureRtType(module, cliSecureRtType);
			newOne.stringDecrypter = new StringDecrypter(module, stringDecrypter);
			newOne.resourceDecrypter = new ResourceDecrypter(module, resourceDecrypter);
			newOne.proxyCallFixer = new ProxyCallFixer(module, proxyCallFixer);
			newOne.csvmV1 = new vm.v1.Csvm(DeobfuscatedFile.DeobfuscatorContext, module, csvmV1);
			newOne.csvmV2 = new vm.v2.Csvm(DeobfuscatedFile.DeobfuscatorContext, module, csvmV2);
			return newOne;
		}

		static List<TypeDef> Lookup(ModuleDefMD module, List<TypeDef> types, string errorMsg) {
			var list = new List<TypeDef>(types.Count);
			foreach (var type in types)
				list.Add(DeobUtils.Lookup(module, type, errorMsg));
			return list;
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			cliSecureRtType.FindStringDecrypterMethod();
			stringDecrypter.AddDecrypterInfos(cliSecureRtType.StringDecrypterInfos);
			stringDecrypter.Initialize();

			AddAttributesToBeRemoved(cliSecureAttributes, "Obfuscator attribute");

			if (options.DecryptResources) {
				DecryptResources(resourceDecrypter);
				AddCctorInitCallToBeRemoved(resourceDecrypter.RsrcRrrMethod);
			}

			stackFrameHelper = new StackFrameHelper(module);
			stackFrameHelper.Find();

			foreach (var type in module.Types) {
				if (type.FullName == "InitializeDelegate" && DotNetUtils.DerivesFromDelegate(type))
					this.AddTypeToBeRemoved(type, "Obfuscator type");
			}

			proxyCallFixer.Find();

			foreach (var info in stringDecrypter.StringDecrypterInfos)
				staticStringInliner.Add(info.Method, (method, gim, args) => stringDecrypter.Decrypt((string)args[0]));
			DeobfuscatedFile.StringDecryptersAdded();

			if (options.DecryptMethods) {
				AddCctorInitCallToBeRemoved(cliSecureRtType.InitializeMethod);
				AddCctorInitCallToBeRemoved(cliSecureRtType.PostInitializeMethod);
				FindPossibleNamesToRemove(cliSecureRtType.LoadMethod);
			}

			if (options.RestoreVmCode && (csvmV1.Detected || csvmV2.Detected)) {
				if (csvmV1.Detected && csvmV1.Restore())
					AddResourceToBeRemoved(csvmV1.Resource, "CSVM data resource");
				else if (csvmV2.Detected && csvmV2.Restore())
					AddResourceToBeRemoved(csvmV2.Resource, "CSVM data resource");
				else {
					Logger.e("Couldn't restore VM methods. Use --dont-rename or it will not run");
					PreserveTokensAndTypes();
				}
			}
		}

		void DecryptResources(ResourceDecrypter resourceDecrypter) {
			var rsrc = resourceDecrypter.MergeResources();
			if (rsrc == null)
				return;
			AddResourceToBeRemoved(rsrc, "Encrypted resources");
			AddTypeToBeRemoved(resourceDecrypter.Type, "Resource decrypter type");
		}

		public override void DeobfuscateMethodEnd(Blocks blocks) {
			if (Operations.DecryptStrings != OpDecryptString.None)
				stringDecrypter.Deobfuscate(blocks);
			proxyCallFixer.Deobfuscate(blocks);
			RemoveStackFrameHelperCode(blocks);
			base.DeobfuscateMethodEnd(blocks);
		}

		public override void DeobfuscateEnd() {
			if (options.SetInitLocals)
				SetInitLocals();
			RemoveProxyDelegates(proxyCallFixer);
			if (options.RemoveStackFrameHelper) {
				if (stackFrameHelper.ExceptionLoggerRemover.NumRemovedExceptionLoggers > 0)
					AddTypeToBeRemoved(stackFrameHelper.Type, "StackFrameHelper type");
			}
			if (CanRemoveStringDecrypterType) {
				AddTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");
				foreach (var info in stringDecrypter.StringDecrypterInfos) {
					if (info.Method.DeclaringType != cliSecureRtType.Type)
						AddMethodToBeRemoved(info.Method, "String decrypter method");
					if (info.Field != null && info.Field.DeclaringType != stringDecrypter.Type)
						AddFieldToBeRemoved(info.Field, "String decrypter field");
				}
				if (options.DecryptMethods)
					AddTypeToBeRemoved(cliSecureRtType.Type ?? stringDecrypter.KeyArrayFieldType, "Obfuscator type");
			}
			if (options.DecryptMethods) {
				AddResources("Obfuscator protection files");
			}

			base.DeobfuscateEnd();

			// Call hasNativeMethods() after all types/methods/etc have been removed since
			// some of the removed methods could be native methods
			if (!module.IsILOnly && !HasNativeMethods())
				module.IsILOnly = true;
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			var list = new List<int>();
			foreach (var info in stringDecrypter.StringDecrypterInfos)
				list.Add(info.Method.MDToken.ToInt32());
			return list;
		}

		void RemoveStackFrameHelperCode(Blocks blocks) {
			if (!options.RemoveStackFrameHelper)
				return;
			if (stackFrameHelper.ExceptionLoggerRemover.Remove(blocks))
				Logger.v("Removed StackFrameHelper code");
		}
	}
}
