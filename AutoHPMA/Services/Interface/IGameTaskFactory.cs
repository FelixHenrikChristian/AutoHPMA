using AutoHPMA.GameTask;

namespace AutoHPMA.Services.Interface;

public interface IGameTaskFactory
{
    IGameTask CreateAutoClubQuiz(
        nint displayHwnd,
        nint gameHwnd,
        int answerDelay,
        bool joinOthers,
        bool stopWhenContributionFull);

    IGameTask CreateAutoForbiddenForest(
        nint displayHwnd,
        nint gameHwnd,
        int times,
        string teamPosition);

    IGameTask CreateAutoCooking(
        nint displayHwnd,
        nint gameHwnd,
        int times,
        string dish);

    IGameTask CreateAutoSweetAdventure(
        nint displayHwnd,
        nint gameHwnd);
}
