
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
        // issue #77: 初期登録用パスコードを返すAPIは廃止した。
        // パスコードはサーバーコンソールへ出力されるか、
        // デプロイ時に InitialAdmin:Passcode 設定として提供される(IPasscodeService を参照)。

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
        /// <summary>
        /// 管理者によるパスワードリセット(issue #83)。
        /// Identity のリセットトークン経由でパスワードハッシュを1回の更新で置き換えるため、
        /// 処理は原子的に行われる。新しいパスワードの設定に失敗した場合、
        /// 既存のパスワードハッシュは変更されないため、元のパスワードで引き続きログインできる。
        /// 応答・ログにパスワードや秘密情報を含めない。
        /// 対象が Admin ロール保有者の場合は、呼び出し元も Admin ロール保有者である必要がある
        /// (UserManagement 権限を持つロールによる Admin アカウント乗っ取り=権限昇格を防ぐ)。
        /// </summary>
        [Authorize(Policy = "UserView")]
        [Authorize(Policy = "UserManagement")]
        [HttpPost(nameof(ResetPassword))]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordModel resetPasswordModel)
        {
            if (resetPasswordModel.NewPassword != resetPasswordModel.ConfirmPassword)
            {
                return BadRequest(new ApiMessage("新しいパスワードが一致しません"));
            }
            var user = await userManager.FindByNameAsync(resetPasswordModel.UserName);
            if (user == null)
            {
                return NotFound(new ApiMessage("ユーザーが見つかりません"));
            }

            // Admin ロール保有者を対象とする場合は Admin のみに許可(権限昇格対策)
            if (await userManager.IsInRoleAsync(user, "Admin"))
            {
                var caller = await userManager.GetUserAsync(User);
                if (caller == null || !await userManager.IsInRoleAsync(caller, "Admin"))
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        new ApiMessage("Adminロール保有者のパスワード変更にはAdmin権限が必要です"));
                }
            }

            // ResetPasswordAsync は内部的に PasswordHash を単一の Update で置き換えるため、
            // 「削除してから追加」のような中間状態(どちらのパスワードでもログインできない状態)が発生しない
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            var result = await userManager.ResetPasswordAsync(user, resetToken, resetPasswordModel.NewPassword);
            if (!result.Succeeded)
            {
                // 失敗時も既存パスワードは保持される。エラー内容(検証規則違反など)のみを返す
                return BadRequest(result.Errors.ToApiMessage());
            }

            // セキュリティスタンプは ResetPasswordAsync 内で更新される。
            // 既存セッション(当該ユーザーの cookie)は SecurityStampValidator の検証タイミング
            // (既定30分間隔)で無効化されるため、即時には失効しない点に注意。
            // 自分自身のパスワードを変更した場合は SecurityStamp 更新後もセッションを維持する
            var my = await userManager.GetUserAsync(User);
            if (my?.Id == user.Id)
            {
                await signInManager.RefreshSignInAsync(my);
            }
            return Ok();
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

        [Authorize]
        [HttpPost(nameof(ChangePassword))]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordModel model)
        {
            if (model.NewPassword != model.ConfirmPassword)
            {
                return BadRequest(new ApiMessage("新しいパスワードが一致しません"));
            }
            var user = await userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }
            var result = await userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (!result.Succeeded)
            {
                return BadRequest(result.Errors.ToApiMessage());
            }
            // SecurityStamp 更新後もセッションを維持するため Cookie を再発行する
            await signInManager.RefreshSignInAsync(user);
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
