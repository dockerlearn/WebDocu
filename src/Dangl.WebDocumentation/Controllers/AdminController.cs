﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Dangl.WebDocumentation.Models;
using Dangl.WebDocumentation.Services;
using Dangl.WebDocumentation.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Dangl.WebDocumentation.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly IProjectFilesService _projectFilesService;
        private readonly ApplicationDbContext _context;
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IProjectVersionsService _projectVersionsService;
        private readonly IProjectsService _projectsService;

        public AdminController(ApplicationDbContext context,
            IHostingEnvironment hostingEnvironment,
            UserManager<ApplicationUser> userManager,
            IProjectFilesService projectFilesService,
            IProjectVersionsService projectVersionsService,
            IProjectsService projectsService)
        {
            _projectFilesService = projectFilesService;
            _context = context;
            _hostingEnvironment = hostingEnvironment;
            _userManager = userManager;
            _projectVersionsService = projectVersionsService;
            _projectsService = projectsService;
        }

        public IActionResult Index()
        {
            ViewData["Section"] = "Admin";
            var model = new IndexViewModel();
            model.Projects = _context.DocumentationProjects.OrderBy(project => project.Name);
            return View(model);
        }

        public IActionResult CreateProject()
        {
            ViewData["Section"] = "Admin";
            var model = new CreateProjectViewModel();
            model.AvailableUsers = _context.Users.Select(appUser => appUser.UserName).OrderBy(username => username);
            return View(model);
        }

        [HttpPost]
        public IActionResult CreateProject(CreateProjectViewModel model, List<string> selectedUsers)
        {
            ViewData["Section"] = "Admin";
            var usersToAdd = _context.Users.Where(currentUser => selectedUsers.Contains(currentUser.UserName)).ToList();
            if (selectedUsers.Any(selected => usersToAdd.All(foundUser => foundUser.UserName != selected)))
            {
                ModelState.AddModelError("", "Unrecognized user selected");
            }
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var projectToAdd = new DocumentationProject
            {
                IsPublic = model.IsPublic,
                Name = model.ProjectName,
                PathToIndex = model.PathToIndexPage
            };
            _context.DocumentationProjects.Add(projectToAdd);
            _context.SaveChanges();
            if (usersToAdd.Any())
            {
                foreach (var currentUser in usersToAdd)
                {
                    _context.UserProjects.Add(new UserProjectAccess
                    {
                        ProjectId = projectToAdd.Id,
                        UserId = currentUser.Id
                    });
                }
                _context.SaveChanges();
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [Route("EditProject/{projectId}")]
        public IActionResult EditProject(Guid projectId)
        {
            ViewData["Section"] = "Admin";
            var project = _context.DocumentationProjects.FirstOrDefault(curr => curr.Id == projectId);
            if (project == null)
            {
                return NotFound();
            }
            var usersWithAccess = _context.UserProjects.Where(assignment => assignment.ProjectId == project.Id).Select(assignment => assignment.User.Email).ToList();
            var usersWithoutAccess = _context.Users.Select(currentUser => currentUser.Email).Where(currentUser => !usersWithAccess.Contains(currentUser)).ToList();
            var model = new EditProjectViewModel();
            model.ProjectName = project.Name;
            model.IsPublic = project.IsPublic;
            model.PathToIndexPage = project.PathToIndex;
            model.UsersWithAccess = usersWithAccess;
            model.AvailableUsers = usersWithoutAccess;
            model.ApiKey = project.ApiKey;
            return View(model);
        }

        [HttpPost]
        [Route("EditProject/{projectId}")]
        public IActionResult EditProject(Guid projectId, EditProjectViewModel model, List<string> selectedUsers)
        {
            ViewData["Section"] = "Admin";
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var databaseProject = _context.DocumentationProjects.FirstOrDefault(project => project.Id == projectId);
            if (databaseProject == null)
            {
                return NotFound();
            }
            databaseProject.ApiKey = model.ApiKey;
            databaseProject.IsPublic = model.IsPublic;
            databaseProject.Name = model.ProjectName;
            databaseProject.PathToIndex = model.PathToIndexPage;
            _context.SaveChanges();
            var selectedUsersIds = _context.Users
                .Where(user => selectedUsers.Contains(user.Email))
                .Select(user => user.Id)
                .ToList();
            // Add missing users
            var knownUsersInProject = _context.UserProjects.Where(rel => rel.ProjectId == databaseProject.Id).Select(rel => rel.UserId).ToList();
            var usersToAdd = selectedUsersIds.Where(userId => !knownUsersInProject.Contains(userId));
            foreach (var newUserId in usersToAdd)
            {
                _context.UserProjects.Add(new UserProjectAccess {UserId = newUserId, ProjectId = databaseProject.Id});
            }
            // Remove users that no longer have access
            var usersToRemove = _context.UserProjects.Where(assignment => assignment.ProjectId == databaseProject.Id).Where(assignment => !selectedUsersIds.Contains(assignment.UserId));
            _context.UserProjects.RemoveRange(usersToRemove);
            _context.SaveChanges();
            var usersWithAccess = _context.UserProjects.Where(assignment => assignment.ProjectId == databaseProject.Id).Select(assignment => assignment.User.Email).ToList();
            var usersWithoutAccess = _context.Users.Select(currentUser => currentUser.Email).Where(currentUser => !usersWithAccess.Contains(currentUser)).ToList();
            model.UsersWithAccess = usersWithAccess;
            model.AvailableUsers = usersWithoutAccess;
            ViewBag.SuccessMessage = $"Changed project {databaseProject.Name}.";
            return View(model);
        }

        [HttpGet]
        [Route("UploadProject/{projectId}")]
        public IActionResult UploadProject(Guid projectId)
        {
            ViewData["Section"] = "Admin";
            var project = _context.DocumentationProjects.FirstOrDefault(p=> p.Id == projectId);
            if (project == null)
            {
                return NotFound();
            }
            ViewBag.ProjectName = project.Name;
            ViewBag.ApiKey = project.ApiKey;
            return View();
        }

        [HttpPost]
        [Route("UploadProject/{projectId}")]
        public async Task<IActionResult> UploadProject(Guid projectId, string version, IFormFile projectPackage)
        {
            ViewData["Section"] = "Admin";
            if (projectPackage == null)
            {
                ModelState.AddModelError("", "Please select a file to upload.");
                return View();
            }
            if (string.IsNullOrWhiteSpace(version))
            {
                ModelState.AddModelError("", "Please specify a version.");
                return View();
            }
            var projectEntry = _context.DocumentationProjects.FirstOrDefault(project => project.Id == projectId);
            if (projectEntry == null)
            {
                return NotFound();
            }
            // Try to read as zip file
            using (var inputStream = projectPackage.OpenReadStream())
            {
                var uploadResult = await _projectFilesService.UploadProjectPackageAsync(projectEntry.Name, version, inputStream);
                if (!uploadResult)
                {
                    ModelState.AddModelError(string.Empty, "Failed to update the project files");
                    return View();
                }
                ViewBag.SuccessMessage = "Uploaded package.";
                return View();
            }
        }

        [HttpGet]
        [Route("DeleteProject/{projectId}")]
        public IActionResult DeleteProject(Guid projectId)
        {
            ViewData["Section"] = "Admin";
            var project = _context.DocumentationProjects.FirstOrDefault(p => p.Id == projectId);
            if (project == null)
            {
                return NotFound();
            }
            var model = new DeleteProjectViewModel();
            model.ProjectName = project.Name;
            model.ProjectId = project.Id;
            return View(model);
        }

        [HttpGet("Projects/DeleteBetaVersions/{projectName}")]
        public async Task<IActionResult> DeleteBetaVersions(string projectName)
        {
            var previewVersionsToDelete = await _projectVersionsService.GetAllPreviewVersionsExceptFirstAndLastAsync(projectName);
            var model = new DeleteBetaVersionsViewModel
            {
                ProjectName = projectName,
                VersionsToDelete = previewVersionsToDelete
            };
            return View(model);
        }

        [HttpPost("Projects/DeleteBetaVersions/{projectName}")]
        public async Task<IActionResult> ConfirmDeleteBetaVersions(string projectName)
        {
            var projectId = await _projectsService.GetIdForProjectByNameAsync(projectName);
            var previewVersionsToDelete = await _projectVersionsService.GetAllPreviewVersionsExceptFirstAndLastAsync(projectName);
            foreach (var previewVersionToDelete in previewVersionsToDelete)
            {
                await _projectFilesService.DeleteProjectVersionPackageAsync(projectId, previewVersionToDelete);
            }
            ViewBag.SuccessMessage = $"Deleted obsolete beta versions for {projectName}.";
            var model = new DeleteBetaVersionsViewModel
            {
                ProjectName = projectName,
                VersionsToDelete = new List<string>()
            };
            return View(nameof(DeleteBetaVersions),model);
        }

        [HttpPost]
        [Route("DeleteProject/{projectId}")]
        public IActionResult DeleteProject(Guid projectId, DeleteProjectViewModel model)
        {
            ViewData["Section"] = "Admin";
            if (!model.ConfirmDelete)
            {
                ModelState.AddModelError(nameof(model.ConfirmDelete), "Please confirm the deletion by checking the checkbox.");
            }
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var documentationProject = _context.DocumentationProjects.FirstOrDefault(project => project.Id == model.ProjectId);
            if (documentationProject == null)
            {
                return NotFound();
            }
            if (documentationProject.FolderGuid != Guid.Empty)
            {
                // Check if physical files present and if yes, delete them
                var physicalDirectory = Path.Combine(_hostingEnvironment.WebRootPath, "App_Data/" + documentationProject.FolderGuid);
                if (Directory.Exists(physicalDirectory))
                {
                    Directory.Delete(physicalDirectory, true);
                }
            }
            _context.DocumentationProjects.Remove(documentationProject);
            _context.SaveChanges();
            ViewBag.SuccessMessage = $"Deleted project {documentationProject.Name}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [Route("DeleteProjectVersion/{projectId}/{version}")]
        public IActionResult DeleteProjectVersion(Guid projectId, string version)
        {
            ViewData["Section"] = "Admin";
            var project = _context.DocumentationProjects.FirstOrDefault(p => p.Id == projectId);
            if (project == null)
            {
                return NotFound();
            }
            var model = new DeleteProjectVersionViewModel
            {
                ConfirmDelete = false,
                ProjectId = projectId,
                ProjectName = project.Name,
                Version = version
            };
            return View(model);
        }

        [HttpPost]
        [Route("DeleteProjectVersion/{projectId}/{version}")]
        public async Task<IActionResult> DeleteProjectVersion(Guid projectId, string version, DeleteProjectVersionViewModel model)
        {
            ViewData["Section"] = "Admin";
            if (!model.ConfirmDelete)
            {
                ModelState.AddModelError(nameof(model.ConfirmDelete), "Please confirm the deletion by checking the checkbox.");
            }
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var documentationProject = _context.DocumentationProjects.FirstOrDefault(project => project.Id == model.ProjectId);
            if (documentationProject == null)
            {
                return NotFound();
            }
            var deletionResult = await _projectFilesService.DeleteProjectVersionPackageAsync(documentationProject.Id, model.Version);
            if (!deletionResult)
            {
                ModelState.AddModelError("Error", "Could not delete project version.");
                return View(model);
            }
            ViewBag.SuccessMessage = $"Deleted project version {documentationProject.Name}.";
            return RedirectToAction(nameof(Index));
        }

        public IActionResult ManageUsers()
        {
            ViewData["Section"] = "Admin";
            var adminRoleId = _context.Roles.FirstOrDefault(role => role.Name == "Admin").Id;
            var model = new ManageUsersViewModel();
            model.Users = _context.Users.Select(user => new UserAdminRoleViewModel { Name = user.Email, IsAdmin = user.Roles.Any(role => role.RoleId == adminRoleId)});
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> ManageUsers(IEnumerable<string> adminUsers)
        {
            ViewData["Section"] = "Admin";
            var adminRole = _context.Roles.FirstOrDefault(role => role.Name == "Admin");
            if (adminRole == null)
            {
                throw new InvalidDataException("Admin role not found");
            }

            // Remove users that are no longer admin
            var oldAdminsToDelete = (from user in _context.Users
                join userRole in _context.UserRoles on user.Id equals userRole.UserId
                join role in _context.Roles on userRole.RoleId equals role.Id
                where role.Name == adminRole.Name
                      && !adminUsers.Contains(user.Email)
                select new {User = user, UserRole = userRole, Role = role}).ToList();
            foreach (var user in oldAdminsToDelete)
            {
                await _userManager.RemoveFromRoleAsync(user.User, adminRole.Name);
            }

            // Add new admin users
            var newAdminsToAdd = (from user in _context.Users
                where _context.UserRoles.Count(userRole => userRole.UserId == user.Id && userRole.RoleId == adminRole.Id) == 0 // As of 04.01.2016, the EF7 RC1 does translate an errorenous SQL when using .Any() in a sub query here, need to fall back to "Count() == 0"
                      && adminUsers.Contains(user.Email)
                select user).ToList();
            foreach (var user in newAdminsToAdd)
            {
                await _userManager.AddToRoleAsync(user, adminRole.Name);
            }

            ViewBag.SuccessMessage = "Updated users.";
            var model = new ManageUsersViewModel();
            model.Users = _context.Users.Select(websiteUser => new UserAdminRoleViewModel { Name = websiteUser.Email, IsAdmin = websiteUser.Roles.Any(role => role.RoleId == adminRole.Id)});
            return View(model);
        }
    }
}
