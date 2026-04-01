using Microsoft.EntityFrameworkCore;
using Sentrix.EntityModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.Repositories
{
    public class PositionRepository
    {

        //private readonly ApplicationDBContext _context;

        private readonly IDbContextFactory<ApplicationDBContext> _contextFactory;

        public PositionRepository(IDbContextFactory<ApplicationDBContext> context)
        {
            _contextFactory = context;
           
        }

        public async Task UpsertPosition(Positions position)
        {
            try
            {
                await using var _context = await _contextFactory.CreateDbContextAsync();
                bool exists = await _context.Positions.AnyAsync(p =>
                    p.UserId == position.UserId &&
                    p.Symbol == position.Symbol &&
                    p.EntryPrice == position.EntryPrice &&
                    p.Ticket == position.Ticket &&
                    p.CreatedUtc == position.CreatedUtc);

                if (!exists)
                {
                    await _context.Positions.AddAsync(position);
                    await _context.SaveChangesAsync();

                    AlertService.Show("Saved", $"New position added: {position.Symbol} at {position.EntryPrice}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpsertPositionAsync exception: {ex.Message}");
            }
        }
        public async Task MarkPositionDeletedAsync(int userId, string symbol, decimal entryPrice, DateTime createdUtc, int ticket)
        {
            try
            {
                await using var _context = await _contextFactory.CreateDbContextAsync();
                var position = await _context.Positions.FirstOrDefaultAsync(p =>
                    p.UserId == userId &&
                    p.Symbol == symbol &&
                    p.EntryPrice == entryPrice &&
                    p.CreatedUtc == createdUtc &&
                    p.Ticket == ticket &&
                    p.Status != "Deleted");

                if (position != null)
                {
                    position.Status = "Deleted";
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarkPositionDeletedAsync exception: {ex.Message}");
            }
        }

        public async Task< List<Positions>> GetTodayPositionsAsync(int userId)
        {
            try
            {
                await using var _context = await _contextFactory.CreateDbContextAsync();
                DateTime today = DateTime.UtcNow.Date;
                return await _context.Positions
                    .Where(p => p.UserId == userId && p.CreatedUtc.Date == today)
                    .ToListAsync();
            }
            catch (Exception ex)
            {

                Debug.WriteLine($"GetTodayPositionsAsync exception: {ex.Message}");
                return new List<Positions>();
            }
        }

        public async Task<Dictionary<string, int>> GetTodayTradeCountBySessionAsync(int userId)
        {
            try
            {
                await using var _context = await _contextFactory.CreateDbContextAsync();
                DateTime today = DateTime.UtcNow.Date;

                return await _context.Positions
                    .Where(p => p.UserId == userId && p.CreatedUtc.Date == today)
                    .GroupBy(p => p.SessionName)
                    .Select(g => new
                    {
                        Session = g.Key,
                        Count = g.Select(p => p.Symbol + "|" + p.EntryPrice + "|" + p.CreatedUtc)
                                 .Distinct()
                                 .Count()
                    })
                    .ToDictionaryAsync(x => x.Session, x => x.Count);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetTodayTradeCountBySessionAsync exception: {ex.Message}");
                return new Dictionary<string, int>();
            }
        }


        public async Task SaveEventAsync(int userId, string message, string displayDateTime = null)
        {
            try
            {
                await using var _context = await _contextFactory.CreateDbContextAsync();

                TimeOnly eventTime = TimeOnly.FromDateTime(DateTime.Now);
                DateTime tradeDate = DateTime.Today;

                TimeOnly thresholdTime = TimeOnly.FromDateTime(DateTime.Now.AddSeconds(-2));

                // Duplicate guard: same message, same user, same date, within 2 seconds
                bool duplicate = await _context.TradeEvents.AnyAsync(e =>
                    e.UserId == userId &&
                    e.TradeDate == tradeDate &&
                    e.Message == message &&
                    e.EventTime >= thresholdTime);

                if (duplicate) return;

                var tradeEvent = new TradeEvents
                {
                    UserId = userId,
                    TradeDate = tradeDate,
                    EventTime = eventTime,
                    Message = message,
                    DisplayDateTime = string.IsNullOrEmpty(displayDateTime)
                        ? DateTime.Now
                        : DateTime.Parse(displayDateTime)
                };

                await  _context.TradeEvents.AddAsync(tradeEvent);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {

                Debug.WriteLine($"SaveEventAsync exception: {ex.Message}");
            }
        }

        public async Task<List<Sentrix.Models.EventLog>> GetEventsByUser(int userId)
        {
            try
            {
                    await using var _context = await _contextFactory.CreateDbContextAsync();
                var events = await _context.TradeEvents.Where(e => e.UserId == userId).OrderByDescending(e => e.TradeDate).ThenByDescending(e => e.EventTime)
                .Select(e => new Sentrix.Models.EventLog
                {
                    Timestamp = e.EventTime.ToString("HH:mm"),
                    Message = e.Message,
                    DisplayDateTime = e.DisplayDateTime.ToString("yyyy-MM-dd HH:mm:ss")
                })
                .ToListAsync();
                return events;
            }
            catch (Exception)
            {

                throw;
            }
            
        }
    }
}
