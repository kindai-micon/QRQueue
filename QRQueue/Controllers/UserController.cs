
using QRQueue.Models;
using QRQueue.Models.API;
using QRQueue.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace QRQueue.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        ApplicationDbContext applicationDbContext,
        IAuthorityScanService authorityScanService,
        IPasscodeService passcodeService) : ControllerBase
    {
        [HttpGet(nameof(MyInfo))]
        public async Task<ActionResult<SendUser>> MyInfo()
        {

            var user = await userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }
            return await BuildSendUserAsync(user);
        }

        [Authorize("UserView")]
        [HttpGet(nameof(UserInfo))]
        public async Task<ActionResult<SendUser>> UserInfo([FromQuery] string userName)
        {

            var user = await userManager.FindByNameAsync(userName);
            if (user == null)
            {
                return NotFound();
            }
            return await BuildSendUserAsync(user);
        }

        [Authorize("UserManagement")]
        [HttpPost(nameof(DeleteUser))]
        public async Task<IActionResult> DeleteUser([FromBody] string userName)
        {
            var my = await userManager.GetUserAsync(User);
            var user = await userManager.FindByNameAsync(userName);
            var adminUsers = await userManager.GetUsersInRoleAsync("Admin");
            if(adminUsers.Any(x=>x.Id==user.Id) && adminUsers.Count == 1)
            {
                return Conflict(new ApiMessage("AdminUserが一人以上存在する必要があります"));
            }

            var result = await userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                return BadRequest(result.Errors.ToApiMessage());
            }
            if (my.Id == user.Id)
            {
                await signInManager.SignOutAsync();
            }
            return Ok();
        }

        [HttpPost(nameof(LoginByEmail))]
        public async Task<IActionResult> LoginByEmail([FromBody] LoginEmailModel loginModel)
        {
            var user = await userManager.FindByEmailAsync(loginModel.Email);
            if (user == null)
            {
                return NotFound();
            }
            var result = await signInManager.PasswordSignInAsync(user, loginModel.Password, true, false);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiMessage("メールアドレスまたはパスワードが正しくありません"));
            }
            return Ok();
        }

        [HttpPost(nameof(LoginByUserName))]
        public async Task<ActionResult<SendUser>> LoginByUserName([FromBody] LoginNameModel loginModel)
        {
            var user = await userManager.FindByNameAsync(loginModel.UserName);
            if (user == null)
            {
                return NotFound(new ApiMessage("ユーザーが見つかりません"));
            }
            var result = await signInManager.PasswordSignInAsync(user, loginModel.Password, true, false);
            if (!result.Succeeded)
            {
                return BadRequest(new ApiMessage("ユーザー名またはパスワードが正しくありません"));
            }
            return await BuildSendUserAsync(user);
        }
        [Authorize(Policy = "UserView")]
        [Authorize(Policy = "UserManagement")]
        [HttpPost(nameof(Register))]
        public async Task<IActionResult> Register([FromBody] RegisterModel registerModel)
        {

            var user = await userManager.FindByNameAsync(registerModel.UserName);
            if (user == null)
            {
                ApplicationUser applicationUser = new ApplicationUser(registerModel.UserName);
                var result = await userManager.CreateAsync(applicationUser, registerModel.Password);
                if (!result.Succeeded)
                {
                    return BadRequest(result.Errors.ToApiMessage());
                }
                if (registerModel.Email != null)
                {
                    result = await userManager.SetEmailAsync(applicationUser, registerModel.Email);

                }
                if (!result.Succeeded)
                {
                    return BadRequest(result.Errors.ToApiMessage());
                }
            }
            else
            {
                return BadRequest(new ApiMessage("存在するユーザー名です"));
            }
            return Ok();
        }
        [HttpGet(nameof(GetPasscode))]
        public ActionResult<PasscodeView> GetPasscode()
        {
            var passcode = passcodeService.GetPasscode();
            Console.WriteLine("passcode:" + passcode);
            return new PasscodeView(passcode);
        }

        [HttpPost(nameof(InitialRegister))]
        public async Task<IActionResult> InitialRegister(InitialUser initialUser)
        {
            if (await passcodeService.CheckPascodeAsync(initialUser.Passcode))
            {
                if (initialUser.Password != initialUser.ConfirmPassword)
                {
                    return BadRequest(new ApiMessage("Passcodeが異なります"));
                }
                ApplicationUser applicationUser = new ApplicationUser();
                applicationUser.UserName = initialUser.UserName;
                applicationUser.Email = initialUser.Email;
                var result = await userManager.CreateAsync(applicationUser, initialUser.Password);
                if (result.Succeeded == false)
                {
                    return BadRequest(result.Errors.ToApiMessage());
                }
                ApplicationRole applicationRole = new ApplicationRole("Admin");
                result = await roleManager.CreateAsync(applicationRole);
                if (result.Succeeded == false)
                {
                    return BadRequest(result.Errors.ToApiMessage());
                }
                List<Authority> authorities = new List<Authority>();
                foreach (var authority in authorityScanService.Authority)
                {
                    var authority1 = applicationDbContext.Authorities.Add(new Authority() { Name = authority });
                    authorities.Add(authority1.Entity);

                }
                applicationRole.Authorities.AddRange(authorities);

                result = await userManager.AddToRoleAsync(applicationUser, applicationRole.Name);
                if (result.Succeeded == false)
                {
                    return BadRequest(result.Errors.ToApiMessage());
                }
                return Ok();
            }
            else
            {
                return BadRequest(new ApiMessage("Passcodeが異なります"));
            }
        }
        [HttpGet(nameof(HasUser))]
        public async Task<ActionResult<bool>> HasUser()
        {
            int count = await userManager.Users.CountAsync();
            return count != 0;
        }
        [Authorize]
        [HttpPost(nameof(Logout))]
        public async Task<IActionResult> Logout()
        {
            await signInManager.SignOutAsync();
            return Ok();
        }

        [Authorize(Policy="UserView")]
        [HttpGet(nameof(UserList))]
        public async Task<ActionResult<List<SendUser>>> UserList()
        {
            var users = (await userManager.Users.ToListAsync());
            List<SendUser> sendUsers = new List<SendUser>();
            foreach (var user in users)
            {
                sendUsers.Add(await BuildSendUserAsync(user));
            }
            return sendUsers;
        }
        [Authorize(Policy = "UserView")]
        [Authorize(Policy = "UserRoleManagement")]
        [HttpPut(nameof(AddRole))]
        public async Task<IActionResult> AddRole([FromBody] UserRoleModel userRoleModel)
        {
            var user = await userManager.FindByNameAsync(userRoleModel.UserName);
            if (user == null)
            {
                return NotFound();
            }
            var role = await roleManager.FindByNameAsync(userRoleModel.RoleName);
            if (role == null)
            {
                return NotFound();
            }
            var result = await userManager.AddToRoleAsync(user, role.Name);
            if (!result.Succeeded)
            {
                return BadRequest(result.Errors.ToApiMessage());
            }
            return Ok();
        }
        [Authorize(Policy = "UserView")]
        [Authorize(Policy = "UserRoleManagement")]
        [HttpPut(nameof(RemoveRole))]
        public async Task<IActionResult> RemoveRole([FromBody] UserRoleModel userRoleModel)
        {
            var users = await userManager.GetUsersInRoleAsync(userRoleModel.RoleName);
            if (userRoleModel.RoleName == "Admin" && users.Count <= 1)
            {
                return Conflict();
            }
            var user = await userManager.FindByNameAsync(userRoleModel.UserName);
            if (user == null)
            {
                return NotFound();
            }
            var role = await roleManager.FindByNameAsync(userRoleModel.RoleName);
            if (role == null)
            {
                return NotFound();
            }
            var result = await userManager.RemoveFromRoleAsync(user, role.Name);
            if (!result.Succeeded)
            {
                return BadRequest(result.Errors.ToApiMessage());
            }
            return Ok();

        }

        /// <summary>ユーザーと付与ロール(権限込み)から SendUser を組み立てる共通処理</summary>
        private async Task<SendUser> BuildSendUserAsync(ApplicationUser user)
        {
            var sendUser = new SendUser(user);
            var roleStrList = await userManager.GetRolesAsync(user);
            sendUser.Roles =
                await roleManager.Roles.Include(x => x.Authorities).Where(x => roleStrList.Contains(x.Name))
                .Select(r => new SendRole(r))
                .ToListAsync();
            return sendUser;
        }
    }
}
