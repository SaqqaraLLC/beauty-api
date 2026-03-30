using Beauty.Api.Data;
using Beauty.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class EmployeesController : ControllerBase
{
    private readonly BeautyDbContext _db;
    public EmployeesController(BeautyDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Employee>>> GetAll() =>
        await _db.Employees.AsNoTracking().OrderBy(e => e.LastName).ToListAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Employee>> GetById(int id)
    {
        var e = await _db.Employees.FindAsync(id);
        return e is null ? NotFound() : e;
    }

    [HttpPost]
    [Authorize] // tighten as needed (e.g., [Authorize(Roles="Admin")])
    public async Task<ActionResult<Employee>> Create(Employee dto)
    {
        _db.Employees.Add(dto);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:int}")]
    [Authorize]
    public async Task<IActionResult> Update(int id, Employee dto)
    {
        if (id != dto.Id) return BadRequest();
        _db.Entry(dto).State = EntityState.Modified;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var e = await _db.Employees.FindAsync(id);
        if (e is null) return NotFound();
        _db.Employees.Remove(e);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
