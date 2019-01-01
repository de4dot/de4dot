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

namespace de4dot.blocks.cflow {
	/// <summary>
	/// Checks whether a type has access to some other target type, method or field
	/// according to the target's visibility.
	/// </summary>
	public struct AccessChecker {
		TypeDef userType;
		List<TypeDef> userTypeEnclosingTypes;
		bool enclosingTypesInitialized;
		Dictionary<IType, bool> baseTypes;
		bool baseTypesInitialized;

		[Flags]
		enum CheckTypeAccess {
			/// <summary>
			/// Can't access the type
			/// </summary>
			None = 0,

			/// <summary>
			/// Normal access to the type and its members. Type + member must be public, internal
			/// or protected (for sub classes) to access the member.
			/// </summary>
			Normal = 1,

			/// <summary>
			/// Full access to the type, even if the type is private. If clear, the type
			/// must be public, internal or protected (for sub classes).
			/// </summary>
			FullTypeAccess = 2,

			/// <summary>
			/// Full access to the type's members (types, fields, methods), even if the
			/// members are private. If clear, the members must be public, internal
			/// or protected (for sub classes)
			/// </summary>
			FullMemberAccess = 4,

			/// <summary>
			/// Full access to the type and its members
			/// </summary>
			Full = Normal | FullTypeAccess | FullMemberAccess,
		}

		/// <summary>
		/// Gets/sets the user type which is accessing the target type, field or method
		/// </summary>
		public TypeDef UserType {
			get => userType;
			set {
				if (userType == value)
					return;
				userType = value;
				enclosingTypesInitialized = false;
				baseTypesInitialized = false;
				if (userTypeEnclosingTypes != null)
					userTypeEnclosingTypes.Clear();
				if (baseTypes != null)
					baseTypes.Clear();
			}
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="userType">The type accessing the target type, field or method</param>
		public AccessChecker(TypeDef userType) {
			this.userType = userType;
			userTypeEnclosingTypes = null;
			baseTypes = null;
			enclosingTypesInitialized = false;
			baseTypesInitialized = false;
		}

		/// <summary>
		/// Checks whether it can access a method or a field
		/// </summary>
		/// <param name="op">Operand</param>
		/// <returns><c>true</c> if it has access to it, <c>false</c> if not, and <c>null</c>
		/// if we can't determine it (eg. we couldn't resolve a type or input was <c>null</c>)</returns>
		public bool? CanAccess(object op) {
			if (op is MethodDef md)
				return CanAccess(md);

			if (op is MemberRef mr)
				return CanAccess(mr);

			if (op is FieldDef fd)
				return CanAccess(fd);

			if (op is MethodSpec ms)
				return CanAccess(ms);

			if (op is TypeRef tr)
				return CanAccess(tr.Resolve());

			if (op is TypeDef td)
				return CanAccess(td);

			if (op is TypeSpec ts)
				return CanAccess(ts);

			return null;
		}

		/// <summary>
		/// Checks whether it can access a <see cref="TypeRef"/>
		/// </summary>
		/// <param name="tr">The type</param>
		/// <returns><c>true</c> if it has access to it, <c>false</c> if not, and <c>null</c>
		/// if we can't determine it (eg. we couldn't resolve a type or input was <c>null</c>)</returns>
		public bool? CanAccess(TypeRef tr) {
			if (tr == null)
				return null;
			return CanAccess(tr.Resolve());
		}

		/// <summary>
		/// Checks whether it can access a <see cref="TypeDef"/>
		/// </summary>
		/// <param name="td">The type</param>
		/// <returns><c>true</c> if it has access to it, <c>false</c> if not, and <c>null</c>
		/// if we can't determine it (eg. we couldn't resolve a type or input was <c>null</c>)</returns>
		public bool? CanAccess(TypeDef td) {
			var access = GetTypeAccess(td, null);
			if (access == null)
				return null;
			return (access.Value & CheckTypeAccess.Normal) != 0;
		}

		/// <summary>
		/// Returns the access we have to <paramref name="td"/>. If <paramref name="td"/> is
		/// enclosing this type, we have private access to it and all its members. If its
		/// declaring type encloses us, we have private access to it, but only normal access
		/// to its members. Else, we only have normal access to it and its members. If we inherit
		/// it, we have protected access to it and its members.
		/// </summary>
		/// <param name="td">The type</param>
		/// <param name="git">Generic instance of <paramref name="td"/> or <c>null</c> if none</param>
		CheckTypeAccess? GetTypeAccess(TypeDef td, GenericInstSig git) {
			if (td == null)
				return null;
			if (userType == td)
				return CheckTypeAccess.Full;

			// If this is our nested type, we have private access to it itself, but normal
			// access to its members.
			if (td.DeclaringType == userType)
				return CheckTypeAccess.Normal | CheckTypeAccess.FullTypeAccess;

			// If we're not a nested type, td can't be our enclosing type
			if (userType.DeclaringType == null)
				return GetTypeAccess2(td, git);

			// Can't be an enclosing type if they're not in the same module
			if (userType.Module != td.Module)
				return GetTypeAccess2(td, git);

			var tdEncTypes = GetEnclosingTypes(td, true);
			var ourEncTypes = InitializeOurEnclosingTypes();
			int maxChecks = Math.Min(tdEncTypes.Count, ourEncTypes.Count);
			int commonIndex;
			for (commonIndex = 0; commonIndex < maxChecks; commonIndex++) {
				if (tdEncTypes[commonIndex] != ourEncTypes[commonIndex])
					break;
			}

			// If td encloses us, then we have access to td and all its members even if
			// they're private.
			if (commonIndex == tdEncTypes.Count)
				return CheckTypeAccess.Full;

			// If there are no common enclosing types, only check the visibility.
			if (commonIndex == 0)
				return GetTypeAccess2(td, git);

			// If td's declaring type encloses this, then we have full access to td even if
			// it's private, but only normal access to its members.
			if (commonIndex + 1 == tdEncTypes.Count)
				return CheckTypeAccess.Normal | CheckTypeAccess.FullTypeAccess;

			// Normal visibility checks starting from type after common enclosing type.
			// Note that we have full access to it so we don't need to check its access,
			// so start from the next one.
			for (int i = commonIndex + 1; i < tdEncTypes.Count; i++) {
				if (!IsVisible(tdEncTypes[i], null))
					return CheckTypeAccess.None;
			}
			return CheckTypeAccess.Normal;
		}

		CheckTypeAccess GetTypeAccess2(TypeDef td, GenericInstSig git) {
			while (td != null) {
				var declType = td.DeclaringType;
				if (userType != declType && !IsVisible(td, git))
					return CheckTypeAccess.None;
				td = declType;
				git = null;
			}
			return CheckTypeAccess.Normal;
		}

		/// <summary>
		/// Checks whether <paramref name="td"/> is visible to us without checking whether they
		/// have any common enclosing types.
		/// </summary>
		/// <param name="td">Type</param>
		/// <param name="git">Generic instance of <paramref name="td"/> or <c>null</c> if none</param>
		bool IsVisible(TypeDef td, GenericInstSig git) {
			if (td == null)
				return false;
			if (td == userType)
				return true;

			switch (td.Visibility) {
			case TypeAttributes.NotPublic:
				return IsSameAssemblyOrFriendAssembly(td.Module);

			case TypeAttributes.Public:
				return true;

			case TypeAttributes.NestedPublic:
				return true;

			case TypeAttributes.NestedPrivate:
				return false;

			case TypeAttributes.NestedFamily:
				return CheckFamily(td, git);

			case TypeAttributes.NestedAssembly:
				return IsSameAssemblyOrFriendAssembly(td.Module);

			case TypeAttributes.NestedFamANDAssem:
				return IsSameAssemblyOrFriendAssembly(td.Module) &&
					CheckFamily(td, git);

			case TypeAttributes.NestedFamORAssem:
				return IsSameAssemblyOrFriendAssembly(td.Module) ||
					CheckFamily(td, git);

			default:
				return false;
			}
		}

		bool IsSameAssemblyOrFriendAssembly(ModuleDef module) {
			if (module == null)
				return false;
			var userModule = userType.Module;
			if (userModule == null)
				return false;
			if (userModule == module)
				return true;
			var userAsm = userModule.Assembly;
			var modAsm = module.Assembly;
			if (IsSameAssembly(userAsm, modAsm))
				return true;
			if (userAsm != null && userAsm.IsFriendAssemblyOf(modAsm))
				return true;

			return false;
		}

		static bool IsSameAssembly(IAssembly asm1, IAssembly asm2) {
			if (asm1 == null || asm2 == null)
				return false;
			if (asm1 == asm2)
				return true;
			return new AssemblyNameComparer(AssemblyNameComparerFlags.All).Equals(asm1, asm2);
		}

		/// <summary>
		/// Checks whether <see cref="userType"/> has access to <paramref name="td"/>.
		/// <paramref name="td"/> is Family, FamANDAssem, or FamORAssem.
		/// </summary>
		/// <param name="td">Type</param>
		/// <param name="git">Generic instance of <paramref name="td"/> or <c>null</c> if none</param>
		bool CheckFamily(TypeDef td, GenericInstSig git) {
			if (td == null)
				return false;
			InitializeBaseTypes();

			if (baseTypes.ContainsKey(git ?? (IType)td))
				return true;

			// td is Family, FamANDAssem, or FamORAssem. If we derive from its enclosing type,
			// we have access to it.
			var td2 = td.DeclaringType;
			if (td2 != null && baseTypes.ContainsKey(td2))
				return true;

			// If one of our enclosing types derive from it, we also have access to it
			var userDeclType = userType.DeclaringType;
			if (userDeclType != null)
				return new AccessChecker(userDeclType).CheckFamily(td, git);

			return false;
		}

		void InitializeBaseTypes() {
			if (baseTypesInitialized)
				return;
			if (baseTypes == null)
				baseTypes = new Dictionary<IType, bool>(TypeEqualityComparer.Instance);
			baseTypesInitialized = true;

			ITypeDefOrRef baseType = userType;
			while (baseType != null) {
				baseTypes[baseType] = true;
				baseType = baseType.GetBaseType();
			}
		}

		List<TypeDef> InitializeOurEnclosingTypes() {
			if (!enclosingTypesInitialized) {
				userTypeEnclosingTypes = GetEnclosingTypes(userType, true);
				enclosingTypesInitialized = true;
			}
			return userTypeEnclosingTypes;
		}

		/// <summary>
		/// Returns a list of all enclosing types, in order of non-enclosed to most enclosed type
		/// </summary>
		/// <param name="td">Type</param>
		/// <param name="includeInput"><c>true</c> if <paramref name="td"/> should be included</param>
		/// <returns>A list of all enclosing types</returns>
		static List<TypeDef> GetEnclosingTypes(TypeDef td, bool includeInput) {
			var list = new List<TypeDef>();
			if (includeInput && td != null)
				list.Add(td);
			while (td != null) {
				var dt = td.DeclaringType;
				if (dt == null)
					break;
				if (list.Contains(dt))
					break;
				list.Add(dt);
				td = dt;
			}
			list.Reverse();
			return list;
		}

		/// <summary>
		/// Checks whether it can access a <see cref="FieldDef"/>
		/// </summary>
		/// <param name="fd">The field</param>
		/// <returns><c>true</c> if it has access to it, <c>false</c> if not, and <c>null</c>
		/// if we can't determine it (eg. we couldn't resolve a type or input was <c>null</c>)</returns>
		public bool? CanAccess(FieldDef fd) => CanAccess(fd, null);

		bool? CanAccess(FieldDef fd, GenericInstSig git) {
			if (fd == null)
				return null;
			var access = GetTypeAccess(fd.DeclaringType, git);
			if (access == null)
				return null;
			var acc = access.Value;
			if ((acc & CheckTypeAccess.Normal) == 0)
				return false;
			if ((acc & CheckTypeAccess.FullMemberAccess) != 0)
				return true;

			return IsVisible(fd, git);
		}

		bool IsVisible(FieldDef fd, GenericInstSig git) {
			if (fd == null)
				return false;
			var fdDeclaringType = fd.DeclaringType;
			if (fdDeclaringType == null)
				return false;
			if (userType == fdDeclaringType)
				return true;

			switch (fd.Access) {
			case FieldAttributes.PrivateScope:
				// Private scope aka compiler controlled fields/methods can only be accessed
				// by a Field/Method token. This means they must be in the same module.
				return userType.Module == fdDeclaringType.Module;

			case FieldAttributes.Private:
				return false;

			case FieldAttributes.FamANDAssem:
				return IsSameAssemblyOrFriendAssembly(fdDeclaringType.Module) &&
					CheckFamily(fdDeclaringType, git);

			case FieldAttributes.Assembly:
				return IsSameAssemblyOrFriendAssembly(fdDeclaringType.Module);

			case FieldAttributes.Family:
				return CheckFamily(fdDeclaringType, git);

			case FieldAttributes.FamORAssem:
				return IsSameAssemblyOrFriendAssembly(fdDeclaringType.Module) ||
					CheckFamily(fdDeclaringType, git);

			case FieldAttributes.Public:
				return true;

			default:
				return false;
			}
		}

		/// <summary>
		/// Checks whether it can access a <see cref="MethodDef"/>
		/// </summary>
		/// <param name="md">The method</param>
		/// <returns><c>true</c> if it has access to it, <c>false</c> if not, and <c>null</c>
		/// if we can't determine it (eg. we couldn't resolve a type or input was <c>null</c>)</returns>
		public bool? CanAccess(MethodDef md) => CanAccess(md, (GenericInstSig)null);

		bool? CanAccess(MethodDef md, GenericInstSig git) {
			if (md == null)
				return null;
			var access = GetTypeAccess(md.DeclaringType, git);
			if (access == null)
				return null;
			var acc = access.Value;
			if ((acc & CheckTypeAccess.Normal) == 0)
				return false;
			if ((acc & CheckTypeAccess.FullMemberAccess) != 0)
				return true;

			return IsVisible(md, git);
		}

		bool IsVisible(MethodDef md, GenericInstSig git) {
			if (md == null)
				return false;
			var mdDeclaringType = md.DeclaringType;
			if (mdDeclaringType == null)
				return false;
			if (userType == mdDeclaringType)
				return true;

			switch (md.Access) {
			case MethodAttributes.PrivateScope:
				// Private scope aka compiler controlled fields/methods can only be accessed
				// by a Field/Method token. This means they must be in the same module.
				return userType.Module == mdDeclaringType.Module;

			case MethodAttributes.Private:
				return false;

			case MethodAttributes.FamANDAssem:
				return IsSameAssemblyOrFriendAssembly(mdDeclaringType.Module) &&
					CheckFamily(mdDeclaringType, git);

			case MethodAttributes.Assembly:
				return IsSameAssemblyOrFriendAssembly(mdDeclaringType.Module);

			case MethodAttributes.Family:
				return CheckFamily(mdDeclaringType, git);

			case MethodAttributes.FamORAssem:
				return IsSameAssemblyOrFriendAssembly(mdDeclaringType.Module) ||
					CheckFamily(mdDeclaringType, git);

			case MethodAttributes.Public:
				return true;

			default:
				return false;
			}
		}

		/// <summary>
		/// Checks whether it can access a <see cref="MemberRef"/>
		/// </summary>
		/// <param name="mr">The member reference</param>
		/// <returns><c>true</c> if it has access to it, <c>false</c> if not, and <c>null</c>
		/// if we can't determine it (eg. we couldn't resolve a type or input was <c>null</c>)</returns>
		public bool? CanAccess(MemberRef mr) {
			if (mr == null)
				return null;

			var parent = mr.Class;

			if (parent is TypeDef td)
				return CanAccess(td, mr);

			if (parent is TypeRef tr)
				return CanAccess(tr.Resolve(), mr);

			if (parent is TypeSpec ts)
				return CanAccess(ts.ResolveTypeDef(), ts.TryGetGenericInstSig(), mr);

			if (parent is MethodDef md)
				return CanAccess(md, mr);

			if (parent is ModuleRef mod)
				return CanAccess(mod, mr);

			return null;
		}

		bool? CanAccess(TypeDef td, MemberRef mr) => CanAccess(td, null, mr);

		bool? CanAccess(TypeDef td, GenericInstSig git, MemberRef mr) {
			if (mr == null || td == null)
				return null;

			if (mr.MethodSig != null) {
				var md = td.FindMethodCheckBaseType(mr.Name, mr.MethodSig);
				if (md == null) {
					// Assume that it's an array type if it's one of these methods
					if (mr.Name == "Get" || mr.Name == "Set" || mr.Name == "Address" || mr.Name == ".ctor")
						return true;
					return null;
				}
				return CanAccess(md, git);
			}

			if (mr.FieldSig != null)
				return CanAccess(td.FindFieldCheckBaseType(mr.Name, mr.FieldSig), git);

			return null;
		}

		bool? CanAccess(MethodDef md, MemberRef mr) {
			if (mr == null || md == null)
				return null;
			return CanAccess(md);
		}

		bool? CanAccess(ModuleRef mod, MemberRef mr) {
			if (mr == null || mod == null || mod.Module == null)
				return null;

			var userModule = userType.Module;
			if (userModule == null)
				return null;
			var userAsm = userModule.Assembly;
			if (!IsSameAssembly(userAsm, mod.Module.Assembly))
				return false;
			if (userAsm == null)
				return false;
			var otherMod = userAsm.FindModule(mod.Name);
			if (otherMod == null)
				return false;
			return CanAccess(otherMod.GlobalType, mr);
		}

		/// <summary>
		/// Checks whether it can access a <see cref="TypeSpec"/>
		/// </summary>
		/// <param name="ts">The type spec</param>
		/// <returns><c>true</c> if it has access to it, <c>false</c> if not, and <c>null</c>
		/// if we can't determine it (eg. we couldn't resolve a type or input was <c>null</c>)</returns>
		public bool? CanAccess(TypeSpec ts) => CanAccess(ts.ResolveTypeDef());

		/// <summary>
		/// Checks whether it can access a <see cref="MethodSpec"/>
		/// </summary>
		/// <param name="ms">The method spec</param>
		/// <returns><c>true</c> if it has access to it, <c>false</c> if not, and <c>null</c>
		/// if we can't determine it (eg. we couldn't resolve a type or input was <c>null</c>)</returns>
		public bool? CanAccess(MethodSpec ms) {
			if (ms == null)
				return null;

			var mdr = ms.Method;

			if (mdr is MethodDef md)
				return CanAccess(md);

			if (mdr is MemberRef mr)
				return CanAccess(mr);

			return null;
		}

		/// <inheritdoc/>
		public override string ToString() => userType.ToString();
	}
}
