using Abp.Authorization;
using Abp.AutoMapper;
using Abp.Collections.Extensions;
using Abp.Domain.Repositories;
using Abp.Localization;
using Abp.UI;
using Castle.Core.Internal;
using Microsoft.AspNet.Identity;
using NorthLion.Zero.Authorization;
using NorthLion.Zero.Authorization.Roles;
using NorthLion.Zero.Authorization.Users;
using NorthLion.Zero.PaginatedModel;
using NorthLion.Zero.Users.Dto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NorthLion.Zero.Users
{
    [AbpAuthorize(PermissionNames.Pages_Users)]
    public class UserAppService : ZeroAppServiceBase, IUserAppService
    {
        private readonly IRepository<User, long> _userRepository;
        private readonly IPermissionManager _permissionManager;
        private readonly RoleManager _roleManager;

        public UserAppService(IRepository<User, long> userRepository, IPermissionManager permissionManager, RoleManager roleManager)
        {
            _userRepository = userRepository;
            _permissionManager = permissionManager;
            _roleManager = roleManager;
        }

        public async Task ProhibitPermission(ProhibitPermissionInput input)
        {
            var user = await UserManager.GetUserByIdAsync(input.UserId);
            var permission = _permissionManager.GetPermission(input.PermissionName);

            await UserManager.ProhibitPermissionAsync(user, permission);
        }

        //Example for primitive method parameters.
        public async Task RemoveUserFromRole(long userId, string roleName)
        {
            CheckErrors(await UserManager.RemoveFromRoleAsync(userId, roleName));
        }

        public async Task<UsersOutput> GetUsers(PaginatedInputDto input)
        {
            //This is only for demonstration purposes need perf tweaks
            if (input.GetAll) return AllResults;
            //--------------------------------------------------------
            await Task.FromResult(0); //Fake Async

            var pagesToSkip = PaginationHelpers.GetSkipTotal(input.Page, input.RowsPerPage);
            //Possible specification pattern required
            var users = GetUsersByStringFilter(_userRepository.GetAll(), input.SearchString, input.Filter);

            users = GetSortedUsers(input.Sort, input.SortDir, users);
            var usersListEnum = users as IList<User> ?? users.ToList();

            var totalPages = PaginationHelpers.GetRemainingPages(usersListEnum.Count(), input.RowsPerPage);

            var usersList = usersListEnum.Skip(pagesToSkip)
                .Take(input.RowsPerPage).ToList();
            return new UsersOutput() //Implements IPaginableResult
            {
                RemainingPages = totalPages,
                Page = input.Page,
                Rows = input.RowsPerPage,
                SearchString = input.SearchString,
                Users = usersList.Select(a => a.MapTo<UserListDto>()).ToList()
            };


        }



        public async Task CreateUser(CreateUserInput input)
        {
            var user = input.MapTo<User>();

            user.TenantId = AbpSession.TenantId;
            user.Password = new PasswordHasher().HashPassword(input.Password);
            user.IsEmailConfirmed = true;

            CheckErrors(await UserManager.CreateAsync(user));
            await CurrentUnitOfWork.SaveChangesAsync();
            await AssignDefaultRoles(user.Id);

        }

        public async Task UpdateUserProfile(EditProfileInput input)
        {
            var userFound = await GetCurrentUserAsync();
            var modified = input.MapTo(userFound);
            await UserManager.UpdateAsync(modified);
        }

        public async Task EditUser(UpdateUserInput input)
        {
            var userFound = _userRepository.Get(input.Id);
            var modified = input.MapTo(userFound);
            await UserManager.UpdateAsync(modified);
            //Notify user by email or something
        }

        public async Task<UpdateUserInput> GetUserForEdit(long? userId)
        {
            if (!userId.HasValue) return new UpdateUserInput();
            var user = await UserManager.GetUserByIdAsync(userId.Value);
            var input = user.MapTo<UpdateUserInput>();
            return input;
        }

        public async Task<UserRoleSelectorOutput> GetRolesForUser(long userId)
        {
            var userRoles = await UserManager.GetRolesAsync(userId);
            var allRoles = _roleManager.Roles.ToList();
            var checkRoles = GetActiveAndInactiveRoles(userRoles, allRoles);
            var user = await UserManager.GetUserByIdAsync(userId);
            return new UserRoleSelectorOutput()
            {
                UserId = user.Id,
                Roles = checkRoles,
            };
        }

        public async Task<UsersOutput> GetUsersInRole(string roleName)
        {
            var role = await _roleManager.GetRoleByNameAsync(roleName);
            if (role == null) new UsersOutput() { };
            var usersInRole = _userRepository.GetAllIncluding(a => a.Roles).Where(r => r.Roles.Any(a => a.RoleId == role.Id)).ToList();
            return new UsersOutput()
            {
                Users = usersInRole.Select(a => a.MapTo<UserListDto>()).ToList()
            };

        }
        public async Task SetUserRoles(SetUserRolesInput input)
        {
            var user = await UserManager.GetUserByIdAsync(input.UserId);
            await UserManager.SetRoles(user, input.Roles.ToArray());
            //Notify user by email or something
        }

        public async Task<EditProfileInput> GetUserProfileForEdit()
        {
            var user = await GetCurrentUserAsync();
            var userProfileInfo = user.MapTo<EditProfileInput>();
            userProfileInfo.MyRoles = (await UserManager.GetRolesAsync(userProfileInfo.Id)).ToList();
            return userProfileInfo;
        }

        public async Task UpdateUserProfilePicture(long userId, string profilePicture)
        {
            var user = await _userRepository.FirstOrDefaultAsync(a => a.Id == userId);
            //Property not implemented for simplicity
            //user.ProfilePicture = profilePicture;
        }

        public async Task ChangeUserPassword(ChangePasswordInput input)
        {
            var user = await GetCurrentUserAsync();

            var hasher = new PasswordHasher();
            if (!string.IsNullOrEmpty(input.CurrentPassword))
            {
                var checkedPassword = hasher.VerifyHashedPassword(user.Password, input.CurrentPassword);
                switch (checkedPassword)
                {
                    case PasswordVerificationResult.Failed:
                        //Is new password
                        throw new UserFriendlyException(L("InvalidPassword"));
                    case PasswordVerificationResult.Success:
                        //Is old password
                        user.Password = hasher.HashPassword(input.NewPassword);
                        await UserManager.UpdateAsync(user);
                        //Notify user by email or something
                        break;
                    case PasswordVerificationResult.SuccessRehashNeeded:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        private List<Permission> AllPermissions
        {
            get
            {
                List<Permission> allPermissions = new List<Permission>();

                if (AbpSession.TenantId == null)
                {
                    //IsHost
                    allPermissions = _permissionManager.GetAllPermissions().Where(a => a.Parent == null).ToList();
                }
                else
                {
                    var permissionsFound = _permissionManager.GetAllPermissions().ToList();
                    allPermissions = permissionsFound.Where(a => a.MultiTenancySides == Abp.MultiTenancy.MultiTenancySides.Tenant || a.MultiTenancySides == Abp.MultiTenancy.MultiTenancySides.Host && a.Parent == null).ToList();
                }
                return allPermissions;
            }
        }
        public async Task<CurrentUserPermissionsOutput> GetUserPermissions(long userId)
        {
            var user = await UserManager.GetUserByIdAsync(userId);
            var userPermissions = (await UserManager.GetGrantedPermissionsAsync(user)).ToList();
            var assignedPermissions = CheckPermissions(AllPermissions, userPermissions).ToList();
            return new CurrentUserPermissionsOutput()
            {
                UserId = userId,
                AssignedPermissions = assignedPermissions
            };
        }

        public async Task ResetPermissions(long userId)
        {
            var user = await UserManager.GetUserByIdAsync(userId);
            await UserManager.ResetAllPermissionsAsync(user);
            //Notify user by email or something
        }

        public async Task UnlockUser(long userId)
        {
            var user = await UserManager.GetUserByIdAsync(userId);
            user.IsLockoutEnabled = false;
            //Notify user by email or something
        }

        public async Task LockUser(long userId)
        {
            var user = await UserManager.GetUserByIdAsync(userId);
            user.IsLockoutEnabled = true;
            //Five days                //You can create a const for this
            user.LockoutEndDateUtc = DateTime.Now.AddDays(5);
            //Notify user by email or something
        }

        public async Task DeleteUser(long userId)
        {
            var userToDelete = await UserManager.GetUserByIdAsync(userId);
            await _userRepository.DeleteAsync(userToDelete);
            //Notify admin by email or something
        }

        public async Task<List<string>> GetPermissions()
        {
            var permissions = await UserManager.GetGrantedPermissionsAsync((await GetCurrentUserAsync()));
            return permissions.Select(a => a.Name).ToList();
        }

        public async Task<List<string>> GetRoles()
        {
            if (AbpSession.UserId == null) return new List<string>();
            var roles = await UserManager.GetRolesAsync(AbpSession.UserId.Value);
            return roles.ToList();
        }
        public async Task SetUserSpecialPermissions(SetUserSpecialPermissionsInput input)
        {
            var user = await UserManager.GetUserByIdAsync(input.UserId);
            foreach (var inputAssignedPermission in input.AssignedPermissions)
            {
                var permission = _permissionManager.GetPermission(inputAssignedPermission.Name);
                if (inputAssignedPermission.Granted)
                {

                    await UserManager.GrantPermissionAsync(user, permission);
                }
                else
                {
                    await UserManager.ProhibitPermissionAsync(user, permission);
                }
            }
            //Notify user by email or something
        }
        public async Task ChangePasswordFromAdmin(ChangePasswordInput input)
        {
            if (input.NewPassword != input.NewPasswordConfirmation) throw new UserFriendlyException(L("PasswordsNotMatch"));
            var user = await UserManager.GetUserByIdAsync(input.UserId);
            var hasher = new PasswordHasher();
            user.Password = hasher.HashPassword(input.NewPassword);
            await UserManager.UpdateAsync(user);
        }
        public async Task<EditProfileInput> GetUserProfile(int id)
        {
            var user = await UserManager.GetUserByIdAsync(id);
            return user.MapTo<EditProfileInput>();
        }
        #region Helpers

        private UsersOutput AllResults
        {
            get
            {
                return new UsersOutput()
                {
                    RemainingPages = 0,
                    Page = 0,
                    Rows = 0,
                    SearchString = "",
                    //This is only a sample
                    Users = _userRepository.GetAll().ToList().Select(a => a.MapTo<UserListDto>()).ToList()
                };
            }

        }
        private IEnumerable<User> GetUsersByStringFilter(IQueryable<User> users, string searchString, string filterProperty)
        {
            Func<User, bool> exp = a => a.UserName.ToUpper().Contains(searchString.ToUpper());
            var usersResult = users
                    .WhereIf(!searchString.IsNullOrEmpty(), exp);
            return usersResult;
        }
        private async Task AssignDefaultRoles(long userId)
        {
            var user = await UserManager.GetUserByIdAsync(userId);
            var roles = _roleManager.Roles.Where(a => a.IsDefault);

            await UserManager.AddToRolesAsync(user.Id, roles.Select(a => a.Name).ToArray());
        }
        private IEnumerable<User> GetSortedUsers(string sort, string sortDir, IEnumerable<User> users)
        {
            switch (sort)
            {
                case "UserName":
                    users = sortDir == "desc" ?
                        users.OrderByDescending(a => a.UserName) :
                        users.OrderBy(a => a.UserName);
                    break;
                case "FullName":
                    users = sortDir == "desc" ?
                        users.OrderByDescending(a => a.FullName) :
                        users.OrderBy(a => a.FullName);
                    break;
                default:
                    users = sortDir == "desc" ?
                        users.OrderByDescending(a => a.Name) :
                        users.OrderBy(a => a.Name);
                    break;
            }
            return users;
        }
        private IEnumerable<UserAssignedPermission> CheckPermissions(IEnumerable<Permission> allPermissions, ICollection<Permission> userPermissions)
        {
            var permissionsFound = new List<UserAssignedPermission>();
            foreach (var permission in allPermissions)
            {
                AddPermission(permissionsFound, userPermissions, permission, userPermissions.Any(a => a.Name == permission.Name));
            }
            return permissionsFound;
        }
        private void AddPermission(ICollection<UserAssignedPermission> permissionsFound, ICollection<Permission> userPermissions, Permission allPermission, bool granted)
        {

            var childPermissions = new List<UserAssignedPermission>();
            var permission = new UserAssignedPermission()
            {
                DisplayName = allPermission.DisplayName.Localize(new LocalizationContext(LocalizationManager)),
                Granted = granted,
                Name = allPermission.Name,
                ParentPermission = allPermission.Parent?.Name
            };
            if (allPermission.Children.Any())
            {
                foreach (var childPermission in allPermission.Children)
                {
                    AddPermission(childPermissions, userPermissions, childPermission, userPermissions.Any(a => a.Name == childPermission.Name));
                }
                permission.ChildPermissions.AddRange(childPermissions);
            }

            permissionsFound.Add(permission);
        }

        private List<UserSelectRoleDto> GetActiveAndInactiveRoles(IList<string> userRoles, IEnumerable<Role> allRoles)
        {
            var roleDtos = new List<UserSelectRoleDto>();
            foreach (var allRole in allRoles)
            {
                roleDtos.Add(new UserSelectRoleDto()
                {
                    DisplayName = allRole.DisplayName,
                    Name = allRole.Name,
                    IsSelected = userRoles.Any(a => a == allRole.Name),
                    IsStatic = allRole.IsStatic
                });
            }
            return roleDtos;
        }





        #endregion
    }
}