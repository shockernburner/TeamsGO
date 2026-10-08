// TETHER: Primal — one finished online run, checked on the server and added to the worldwide totals.
// The same rules as Match/CareerScore.cs and Match/ScoreCheck (keep them in step). Called by the game with the
// player's own sign-in, so the run always goes to the player who sent it.
const { LeaderboardsApi } = require("@unity-services/leaderboards-1.1");
const { DataApi } = require("@unity-services/cloud-save-1.4");

const MIN_SECONDS = 30, MAX_SECONDS = 45 * 60, MAX_POINTS_PER_SECOND = 60, WIPE_PENALTY = 50;
const COUNTRIES = ["US","GB","CA","AU","NZ","IE","DE","FR","ES","IT","NL","BE","SE","NO","DK","FI","PL","PT","BR","MX",
  "AR","CL","RU","UA","TR","IN","PK","BD","LK","SG","MY","ID","PH","TH","VN","JP","KR","TW","HK","CN","SA","AE","EG",
  "NG","ZA"];

module.exports = async ({ params, context, logger }) => {
  const score = Math.floor(Number(params.score) || 0), seconds = Number(params.seconds) || 0;
  const kills = Math.floor(Number(params.kills) || 0), seed = Math.floor(Number(params.seed) || 0);
  const wiped = params.wiped === true, test = params.test === true;
  const name = String(params.player || "").slice(0, 20), crew = String(params.crew || "").slice(0, 24);
  const country = String(params.country || "").toUpperCase();

  // The checks a modified game can't skip.
  if (score < 0 || kills < 0) return { ok: false, reason: "negative values" };
  if (!(seconds >= MIN_SECONDS)) return { ok: false, reason: "match too short" };
  if (seconds > MAX_SECONDS) return { ok: false, reason: "match too long" };
  if (score > seconds * MAX_POINTS_PER_SECOND) return { ok: false, reason: "score too high for the time played" };

  // One result per island, and no faster than matches can be played. Kept in protected player data, which only
  // the server can write, so a player can't clear it to send an island again.
  const save = new DataApi(context);
  const key = test ? "test_lastRun" : "lastRun";
  const now = Math.floor(Date.now() / 1000);
  try {
    const got = await save.getProtectedItems(context.projectId, context.playerId, [key]);
    const last = got.data.results.length ? got.data.results[0].value : null;
    if (last && last.seed === seed) return { ok: false, reason: "this island's result is already in" };
    if (last && now - last.t < seconds * 0.8) return { ok: false, reason: "results are coming in faster than matches can be played" };
  } catch (e) { logger.warning("lastRun read failed", { "error.message": e.message }); }

  const delta = wiped ? -WIPE_PENALTY : score;
  const boards = new LeaderboardsApi(context);
  const pre = test ? "test_" : "";
  const meta = { player: name, crew: crew, country: country };

  // Totals never drop below zero: a penalty takes away at most what's there.
  async function add(boardId) {
    let current = 0;
    try { current = (await boards.getLeaderboardPlayerScore(context.projectId, boardId, context.playerId)).data.score || 0; }
    catch (e) { /* no entry yet */ }
    const change = Math.max(delta, -current);
    if (change === 0 && delta <= 0) return current;
    const res = await boards.addLeaderboardPlayerScore(context.projectId, boardId, context.playerId, { score: change, metadata: meta });
    return res.data.score;
  }

  const total = await add(pre + "survivors_total");
  await add(pre + "survivors_week");
  if (!test && COUNTRIES.includes(country)) {
    try { await add("survivors_total_" + country); } catch (e) { logger.warning("country board", { "error.message": e.message }); }
  }
  await save.setProtectedItem(context.projectId, context.playerId, { key: key, value: { t: now, seed: seed } });
  return { ok: true, delta: delta, total: total };
};

module.exports.params = {
  score: "Numeric", seconds: "Numeric", kills: "Numeric", seed: "Numeric", wiped: "Boolean", test: "Boolean",
  player: "String", crew: "String", country: "String"
};
