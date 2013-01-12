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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.renamer.asmmodules {
	enum ObjectType {
		Unknown,
		EventDef,
		FieldDef,
		GenericParam,
		MemberRef,
		MethodDef,
		MethodSpec,
		PropertyDef,
		TypeDef,
		TypeRef,
		TypeSig,
		TypeSpec,
		ExportedType,
	}

	class MemberRefFinder {
		public Dictionary<CustomAttribute, bool> customAttributes = new Dictionary<CustomAttribute, bool>();
		public Dictionary<EventDef, bool> eventDefs = new Dictionary<EventDef, bool>();
		public Dictionary<FieldDef, bool> fieldDefs = new Dictionary<FieldDef, bool>();
		public Dictionary<GenericParam, bool> genericParams = new Dictionary<GenericParam, bool>();
		public Dictionary<MemberRef, bool> memberRefs = new Dictionary<MemberRef, bool>();
		public Dictionary<MethodDef, bool> methodDefs = new Dictionary<MethodDef, bool>();
		public Dictionary<MethodSpec, bool> methodSpecs = new Dictionary<MethodSpec, bool>();
		public Dictionary<PropertyDef, bool> propertyDefs = new Dictionary<PropertyDef, bool>();
		public Dictionary<TypeDef, bool> typeDefs = new Dictionary<TypeDef, bool>();
		public Dictionary<TypeRef, bool> typeRefs = new Dictionary<TypeRef, bool>();
		public Dictionary<TypeSig, bool> typeSigs = new Dictionary<TypeSig, bool>();
		public Dictionary<TypeSpec, bool> typeSpecs = new Dictionary<TypeSpec, bool>();
		public Dictionary<ExportedType, bool> exportedTypes = new Dictionary<ExportedType, bool>();

		Stack<object> objectStack;
		ModuleDef validModule;

		public void removeTypeDef(TypeDef td) {
			if (!typeDefs.Remove(td))
				throw new ApplicationException(string.Format("Could not remove TypeDef: {0}", td));
		}

		public void removeEventDef(EventDef ed) {
			if (!eventDefs.Remove(ed))
				throw new ApplicationException(string.Format("Could not remove EventDef: {0}", ed));
		}

		public void removeFieldDef(FieldDef fd) {
			if (!fieldDefs.Remove(fd))
				throw new ApplicationException(string.Format("Could not remove FieldDef: {0}", fd));
		}

		public void removeMethodDef(MethodDef md) {
			if (!methodDefs.Remove(md))
				throw new ApplicationException(string.Format("Could not remove MethodDef: {0}", md));
		}

		public void removePropertyDef(PropertyDef pd) {
			if (!propertyDefs.Remove(pd))
				throw new ApplicationException(string.Format("Could not remove PropertyDef: {0}", pd));
		}

		public void findAll(ModuleDef module) {
			validModule = module;

			// This needs to be big. About 2048 entries should be enough for most though...
			objectStack = new Stack<object>(0x1000);

			add(module);
			processAll();

			objectStack = null;
		}

		void push(object mr) {
			if (mr == null)
				return;
			objectStack.Push(mr);
		}

		void processAll() {
			while (objectStack.Count > 0) {
				var o = objectStack.Pop();
				switch (getObjectType(o)) {
				case ObjectType.Unknown: break;
				case ObjectType.EventDef:	add((EventDef)o); break;
				case ObjectType.FieldDef:	add((FieldDef)o); break;
				case ObjectType.GenericParam: add((GenericParam)o); break;
				case ObjectType.MemberRef:	add((MemberRef)o); break;
				case ObjectType.MethodDef:	add((MethodDef)o); break;
				case ObjectType.MethodSpec:	add((MethodSpec)o); break;
				case ObjectType.PropertyDef:add((PropertyDef)o); break;
				case ObjectType.TypeDef:	add((TypeDef)o); break;
				case ObjectType.TypeRef:	add((TypeRef)o); break;
				case ObjectType.TypeSig:	add((TypeSig)o); break;
				case ObjectType.TypeSpec:	add((TypeSpec)o); break;
				case ObjectType.ExportedType: add((ExportedType)o); break;
				default: throw new InvalidOperationException(string.Format("Unknown type: {0}", o.GetType()));
				}
			}
		}

		readonly Dictionary<Type, ObjectType> toObjectType = new Dictionary<Type, ObjectType>();
		ObjectType getObjectType(object o) {
			if (o == null)
				return ObjectType.Unknown;
			var type = o.GetType();
			ObjectType mrType;
			if (toObjectType.TryGetValue(type, out mrType))
				return mrType;
			mrType = getObjectType2(o);
			toObjectType[type] = mrType;
			return mrType;
		}

		static ObjectType getObjectType2(object o) {
			if (o is EventDef)		return ObjectType.EventDef;
			if (o is FieldDef)		return ObjectType.FieldDef;
			if (o is GenericParam)	return ObjectType.GenericParam;
			if (o is MemberRef)		return ObjectType.MemberRef;
			if (o is MethodDef)		return ObjectType.MethodDef;
			if (o is MethodSpec)	return ObjectType.MethodSpec;
			if (o is PropertyDef)	return ObjectType.PropertyDef;
			if (o is TypeDef)		return ObjectType.TypeDef;
			if (o is TypeRef)		return ObjectType.TypeRef;
			if (o is TypeSig)		return ObjectType.TypeSig;
			if (o is TypeSpec)		return ObjectType.TypeSpec;
			if (o is ExportedType)	return ObjectType.ExportedType;
			return ObjectType.Unknown;
		}

		void add(ModuleDef mod) {
			push(mod.ManagedEntryPoint);
			add(mod.CustomAttributes);
			add(mod.Types);
			add(mod.ExportedTypes);
			if (mod.IsManifestModule)
				add(mod.Assembly);
			add(mod.VTableFixups);
		}

		void add(VTableFixups fixups) {
			if (fixups == null)
				return;
			foreach (var fixup in fixups) {
				foreach (var method in fixup)
					push(method);
			}
		}

		void add(AssemblyDef asm) {
			if (asm == null)
				return;
			add(asm.DeclSecurities);
			add(asm.CustomAttributes);
		}

		void add(CallingConventionSig sig) {
			if (sig == null)
				return;

			var fs = sig as FieldSig;
			if (fs != null) {
				add(fs);
				return;
			}

			var mbs = sig as MethodBaseSig;
			if (mbs != null) {
				add(mbs);
				return;
			}

			var ls = sig as LocalSig;
			if (ls != null) {
				add(ls);
				return;
			}

			var gims = sig as GenericInstMethodSig;
			if (gims != null) {
				add(gims);
				return;
			}
		}

		void add(FieldSig sig) {
			if (sig == null)
				return;
			add(sig.Type);
		}

		void add(MethodBaseSig sig) {
			if (sig == null)
				return;
			add(sig.RetType);
			add(sig.Params);
			add(sig.ParamsAfterSentinel);
		}

		void add(LocalSig sig) {
			if (sig == null)
				return;
			add(sig.Locals);
		}

		void add(GenericInstMethodSig sig) {
			if (sig == null)
				return;
			add(sig.GenericArguments);
		}

		void add(IEnumerable<CustomAttribute> cas) {
			if (cas == null)
				return;
			foreach (var ca in cas)
				add(ca);
		}

		void add(CustomAttribute ca) {
			if (ca == null || customAttributes.ContainsKey(ca))
				return;
			customAttributes[ca] = true;
			push(ca.Constructor);
			add(ca.ConstructorArguments);
			add(ca.NamedArguments);
		}

		void add(IEnumerable<CAArgument> args) {
			if (args == null)
				return;
			foreach (var arg in args)
				add(arg);
		}

		void add(CAArgument arg) {
			// It's a struct so can't be null
			add(arg.Type);
		}

		void add(IEnumerable<CANamedArgument> args) {
			if (args == null)
				return;
			foreach (var arg in args)
				add(arg);
		}

		void add(CANamedArgument arg) {
			if (arg == null)
				return;
			add(arg.Type);
			add(arg.Argument);
		}

		void add(IEnumerable<DeclSecurity> decls) {
			if (decls == null)
				return;
			foreach (var decl in decls)
				add(decl);
		}

		void add(DeclSecurity decl) {
			if (decl == null)
				return;
			add(decl.CustomAttributes);
		}

		void add(IEnumerable<EventDef> eds) {
			if (eds == null)
				return;
			foreach (var ed in eds)
				add(ed);
		}

		void add(EventDef ed) {
			if (ed == null || eventDefs.ContainsKey(ed))
				return;
			if (ed.DeclaringType != null && ed.DeclaringType.Module != validModule)
				return;
			eventDefs[ed] = true;
			push(ed.EventType);
			add(ed.CustomAttributes);
			add(ed.AddMethod);
			add(ed.InvokeMethod);
			add(ed.RemoveMethod);
			add(ed.OtherMethods);
			add(ed.DeclaringType);
		}

		void add(IEnumerable<FieldDef> fds) {
			if (fds == null)
				return;
			foreach (var fd in fds)
				add(fd);
		}

		void add(FieldDef fd) {
			if (fd == null || fieldDefs.ContainsKey(fd))
				return;
			if (fd.DeclaringType != null && fd.DeclaringType.Module != validModule)
				return;
			fieldDefs[fd] = true;
			add(fd.CustomAttributes);
			add(fd.Signature);
			add(fd.DeclaringType);
		}

		void add(IEnumerable<GenericParam> gps) {
			if (gps == null)
				return;
			foreach (var gp in gps)
				add(gp);
		}

		void add(GenericParam gp) {
			if (gp == null || genericParams.ContainsKey(gp))
				return;
			genericParams[gp] = true;
			push(gp.Owner);
			push(gp.Kind);
			add(gp.GenericParamConstraints);
			add(gp.CustomAttributes);
		}

		void add(IEnumerable<GenericParamConstraint> gpcs) {
			if (gpcs == null)
				return;
			foreach (var gpc in gpcs)
				add(gpc);
		}

		void add(GenericParamConstraint gpc) {
			if (gpc == null)
				return;
			add(gpc.Owner);
			push(gpc.Constraint);
			add(gpc.CustomAttributes);
		}

		void add(MemberRef mr) {
			if (mr == null || memberRefs.ContainsKey(mr))
				return;
			if (mr.Module != validModule)
				return;
			memberRefs[mr] = true;
			push(mr.Class);
			add(mr.Signature);
			add(mr.CustomAttributes);
		}

		void add(IEnumerable<MethodDef> methods) {
			if (methods == null)
				return;
			foreach (var m in methods)
				add(m);
		}

		void add(MethodDef md) {
			if (md == null || methodDefs.ContainsKey(md))
				return;
			if (md.DeclaringType != null && md.DeclaringType.Module != validModule)
				return;
			methodDefs[md] = true;
			add(md.Signature);
			add(md.ParamDefs);
			add(md.GenericParameters);
			add(md.DeclSecurities);
			add(md.MethodBody);
			add(md.CustomAttributes);
			add(md.Overrides);
			add(md.DeclaringType);
		}

		void add(MethodBody mb) {
			var cb = mb as CilBody;
			if (cb != null)
				add(cb);
		}

		void add(CilBody cb) {
			if (cb == null)
				return;
			add(cb.Instructions);
			add(cb.ExceptionHandlers);
			add(cb.Variables);
		}

		void add(IEnumerable<Instruction> instrs) {
			if (instrs == null)
				return;
			foreach (var instr in instrs) {
				if (instr == null)
					continue;
				switch (instr.OpCode.OperandType) {
				case OperandType.InlineTok:
				case OperandType.InlineType:
				case OperandType.InlineMethod:
				case OperandType.InlineField:
					push(instr.Operand);
					break;

				case OperandType.InlineSig:
					add(instr.Operand as CallingConventionSig);
					break;

				case OperandType.InlineVar:
				case OperandType.ShortInlineVar:
					var local = instr.Operand as Local;
					if (local != null) {
						add(local);
						break;
					}
					var arg = instr.Operand as Parameter;
					if (arg != null) {
						add(arg);
						break;
					}
					break;
				}
			}
		}

		void add(IEnumerable<ExceptionHandler> ehs) {
			if (ehs == null)
				return;
			foreach (var eh in ehs)
				push(eh.CatchType);
		}

		void add(IEnumerable<Local> locals) {
			if (locals == null)
				return;
			foreach (var local in locals)
				add(local);
		}

		void add(Local local) {
			if (local == null)
				return;
			add(local.Type);
		}

		void add(IEnumerable<Parameter> ps) {
			if (ps == null)
				return;
			foreach (var p in ps)
				add(p);
		}

		void add(Parameter param) {
			if (param == null)
				return;
			add(param.Type);
			add(param.Method);
		}

		void add(IEnumerable<ParamDef> pds) {
			if (pds == null)
				return;
			foreach (var pd in pds)
				add(pd);
		}

		void add(ParamDef pd) {
			if (pd == null)
				return;
			add(pd.DeclaringMethod);
			add(pd.CustomAttributes);
		}

		void add(IEnumerable<MethodOverride> mos) {
			if (mos == null)
				return;
			foreach (var mo in mos)
				add(mo);
		}

		void add(MethodOverride mo) {
			// It's a struct so can't be null
			push(mo.MethodBody);
			push(mo.MethodDeclaration);
		}

		void add(MethodSpec ms) {
			if (ms == null || methodSpecs.ContainsKey(ms))
				return;
			if (ms.Method != null && ms.Method.DeclaringType != null && ms.Method.DeclaringType.Module != validModule)
				return;
			methodSpecs[ms] = true;
			push(ms.Method);
			add(ms.Instantiation);
			add(ms.CustomAttributes);
		}

		void add(IEnumerable<PropertyDef> pds) {
			if (pds == null)
				return;
			foreach (var pd in pds)
				add(pd);
		}

		void add(PropertyDef pd) {
			if (pd == null || propertyDefs.ContainsKey(pd))
				return;
			if (pd.DeclaringType != null && pd.DeclaringType.Module != validModule)
				return;
			propertyDefs[pd] = true;
			add(pd.Type);
			add(pd.CustomAttributes);
			add(pd.GetMethod);
			add(pd.SetMethod);
			add(pd.OtherMethods);
			add(pd.DeclaringType);
		}

		void add(IEnumerable<TypeDef> tds) {
			if (tds == null)
				return;
			foreach (var td in tds)
				add(td);
		}

		void add(TypeDef td) {
			if (td == null || typeDefs.ContainsKey(td))
				return;
			if (td.Module != validModule)
				return;
			typeDefs[td] = true;
			push(td.BaseType);
			add(td.Fields);
			add(td.Methods);
			add(td.GenericParameters);
			add(td.Interfaces);
			add(td.DeclSecurities);
			add(td.DeclaringType);
			add(td.Events);
			add(td.Properties);
			add(td.NestedTypes);
			add(td.CustomAttributes);
		}

		void add(IEnumerable<InterfaceImpl> iis) {
			if (iis == null)
				return;
			foreach (var ii in iis)
				add(ii);
		}

		void add(InterfaceImpl ii) {
			if (ii == null)
				return;
			push(ii.Interface);
			add(ii.CustomAttributes);
		}

		void add(TypeRef tr) {
			if (tr == null || typeRefs.ContainsKey(tr))
				return;
			if (tr.Module != validModule)
				return;
			typeRefs[tr] = true;
			push(tr.ResolutionScope);
			add(tr.CustomAttributes);
		}

		void add(IEnumerable<TypeSig> tss) {
			if (tss == null)
				return;
			foreach (var ts in tss)
				add(ts);
		}

		void add(TypeSig ts) {
			if (ts == null || typeSigs.ContainsKey(ts))
				return;
			if (ts.Module != validModule)
				return;
			typeSigs[ts] = true;

			for (; ts != null; ts = ts.Next) {
				switch (ts.ElementType) {
				case ElementType.Void:
				case ElementType.Boolean:
				case ElementType.Char:
				case ElementType.I1:
				case ElementType.U1:
				case ElementType.I2:
				case ElementType.U2:
				case ElementType.I4:
				case ElementType.U4:
				case ElementType.I8:
				case ElementType.U8:
				case ElementType.R4:
				case ElementType.R8:
				case ElementType.String:
				case ElementType.ValueType:
				case ElementType.Class:
				case ElementType.TypedByRef:
				case ElementType.I:
				case ElementType.U:
				case ElementType.Object:
					var tdrs = (TypeDefOrRefSig)ts;
					push(tdrs.TypeDefOrRef);
					break;

				case ElementType.FnPtr:
					var fps = (FnPtrSig)ts;
					add(fps.Signature);
					break;

				case ElementType.GenericInst:
					var gis = (GenericInstSig)ts;
					add(gis.GenericType);
					add(gis.GenericArguments);
					break;

				case ElementType.CModReqd:
				case ElementType.CModOpt:
					var ms = (ModifierSig)ts;
					push(ms.Modifier);
					break;

				case ElementType.End:
				case ElementType.Ptr:
				case ElementType.ByRef:
				case ElementType.Var:
				case ElementType.Array:
				case ElementType.ValueArray:
				case ElementType.R:
				case ElementType.SZArray:
				case ElementType.MVar:
				case ElementType.Internal:
				case ElementType.Module:
				case ElementType.Sentinel:
				case ElementType.Pinned:
				default:
					break;
				}
			}
		}

		void add(TypeSpec ts) {
			if (ts == null || typeSpecs.ContainsKey(ts))
				return;
			if (ts.Module != validModule)
				return;
			typeSpecs[ts] = true;
			add(ts.TypeSig);
			add(ts.CustomAttributes);
		}

		void add(IEnumerable<ExportedType> ets) {
			if (ets == null)
				return;
			foreach (var et in ets)
				add(et);
		}

		void add(ExportedType et) {
			if (et == null || exportedTypes.ContainsKey(et))
				return;
			if (et.Module != validModule)
				return;
			exportedTypes[et] = true;
			add(et.CustomAttributes);
			push(et.Implementation);
		}
	}
}
