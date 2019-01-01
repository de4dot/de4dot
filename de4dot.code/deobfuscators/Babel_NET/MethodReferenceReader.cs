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
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.Babel_NET {
	class BabelMethodreference {
		public string Name { get; set; }
		public TypeSig DeclaringType { get; set; }
		public TypeSig ReturnType { get; set; }
		public Parameter[] Parameters { get; set; }
		public TypeSig[] GenericArguments { get; set; }
		public int Flags { get; set; }
		public bool HasThis => (Flags & 1) != 0;
		public bool IsGenericMethod => (Flags & 2) != 0;
	}

	class BabelMethodDef : BabelMethodreference {
		Parameter thisParameter;

		public int Flags2 { get; set; }
		public ushort MaxStack { get; set; }
		public IList<Local> Locals { get; set; }
		public IList<Instruction> Instructions { get; set; }
		public IList<ExceptionHandler> ExceptionHandlers { get; set; }
		public bool IsStatic => (Flags2 & 0x10) != 0;
		public bool RequiresFatExceptionHandler => (Flags2 & 0x20) != 0;
		public bool InitLocals => (Flags2 & 0x40) != 0;
		public bool CacheMethod => (Flags2 & 0x80) != 0;

		public Parameter ThisParameter {
			get {
				if (!HasThis)
					return null;
				if (thisParameter != null)
					return thisParameter;
				return thisParameter = new Parameter(0, Parameter.HIDDEN_THIS_METHOD_SIG_INDEX, DeclaringType);
			}
		}

		public void SetBody(MethodBodyReader mbr) {
			Flags2 = mbr.Flags2;
			MaxStack = mbr.MaxStack;
			Locals = mbr.Locals;
			Instructions = mbr.Instructions;
			ExceptionHandlers = mbr.ExceptionHandlers;
		}

		public IList<Parameter> GetRealParameters() {
			if (ThisParameter == null)
				return Parameters;
			var parameters = new Parameter[Parameters.Length + 1];
			parameters[0] = ThisParameter;
			Array.Copy(Parameters, 0, parameters, 1, Parameters.Length);
			return parameters;
		}
	}

	class MethodRefReader {
		ImageReader imageReader;
		BabelMethodreference bmr;

		public MethodRefReader(ImageReader imageReader)
			: this(imageReader, new BabelMethodreference()) {
		}

		public MethodRefReader(ImageReader imageReader, BabelMethodreference bmr) {
			this.imageReader = imageReader;
			this.bmr = bmr;
		}

		public BabelMethodreference Read() {
			bmr.Name = imageReader.ReadString();
			bmr.DeclaringType = imageReader.ReadTypeSig();
			bmr.ReturnType = imageReader.ReadTypeSig();
			var argTypes = imageReader.ReadTypeSigs();
			bmr.Flags = imageReader.reader.ReadByte();
			if (bmr.IsGenericMethod)
				bmr.GenericArguments = imageReader.ReadTypeSigs();
			else
				bmr.GenericArguments = new TypeSig[0];
			bmr.Parameters = ReadParameters(argTypes, bmr.HasThis);
			return bmr;
		}

		Parameter[] ReadParameters(IList<TypeSig> argTypes, bool hasThis) {
			var ps = new Parameter[argTypes.Count];
			int bi = hasThis ? 1 : 0;
			for (int i = 0; i < ps.Length; i++)
				ps[i] = new Parameter(bi + i, i, argTypes[i]);
			return ps;
		}
	}

	class MethodDefReader {
		MethodRefReader methodRefReader;
		MethodBodyReader methodBodyReader;
		BabelMethodDef bmd;

		public MethodDefReader(ImageReader imageReader) {
			bmd = new BabelMethodDef();
			methodRefReader = new MethodRefReader(imageReader, bmd);
			methodBodyReader = new MethodBodyReader(imageReader);
		}

		public BabelMethodDef Read() {
			methodRefReader.Read();
			methodBodyReader.Read(bmd.GetRealParameters());
			bmd.SetBody(methodBodyReader);
			return bmd;
		}
	}
}
