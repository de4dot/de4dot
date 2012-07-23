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
using System.Text;
using Mono.Cecil;
using Mono.MyStuff;

namespace de4dot.code.deobfuscators.MaxtoCode {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "MaxtoCode";
		public const string THE_TYPE = "mc";
		const string DEFAULT_REGEX = @"!^[oO01l]+$&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;
		IntOption stringCodePage;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			stringCodePage = new IntOption(null, makeArgName("cp"), "String code page", 936);
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				RenameResourcesInCode = false,
				ValidNameRegex = validNameRegex.get(),
				StringCodePage = stringCodePage.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				stringCodePage,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		MainType mainType;
		DecrypterInfo decrypterInfo;
		StringDecrypter stringDecrypter;

		internal class Options : OptionsBase {
			public int StringCodePage { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		internal Deobfuscator(Options options)
			: base(options) {
			this.options = options;
			StringFeatures = StringFeatures.AllowStaticDecryption | StringFeatures.AllowDynamicDecryption;
		}

		protected override int detectInternal() {
			int val = 0;

			if (mainType.Detected)
				val = 150;

			return val;
		}

		protected override void scanForObfuscator() {
			mainType = new MainType(module);
			mainType.find();
		}

		public override bool getDecryptedModule(ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (!mainType.Detected)
				return false;

			var fileData = DeobUtils.readModule(module);
			decrypterInfo = new DecrypterInfo(mainType, fileData);
			var methodsDecrypter = new MethodsDecrypter(decrypterInfo);

			if (!methodsDecrypter.decrypt(ref dumpedMethods))
				return false;

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			newOne.setModule(module);
			newOne.mainType = new MainType(module, mainType);
			newOne.decrypterInfo = decrypterInfo;
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			stringDecrypter = new StringDecrypter(decrypterInfo);
			stringDecrypter.find();
			if (stringDecrypter.Detected) {
				stringDecrypter.initialize(getEncoding(options.StringCodePage));
				staticStringInliner.add(stringDecrypter.Method, (method, args) => stringDecrypter.decrypt((uint)args[0]));
				DeobfuscatedFile.stringDecryptersAdded();
			}

			foreach (var method in mainType.InitMethods)
				addCctorInitCallToBeRemoved(method);
			addTypeToBeRemoved(mainType.Type, "Obfuscator type");
			addModuleReferencesToBeRemoved(mainType.ModuleReferences, "MC runtime module reference");
		}

		static Encoding getEncoding(int cp) {
			try {
				return Encoding.GetEncoding(cp);
			}
			catch {
				throw new UserException(string.Format("Code page {0} doesn't exist", cp));
			}
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter != null && stringDecrypter.Detected)
				list.Add(stringDecrypter.Method.MetadataToken.ToInt32());
			return list;
		}
	}
}
