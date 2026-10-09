from typing import Any

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel, Field

from app.ai.service import chat
from app.core.security import get_current_user


router = APIRouter(
    prefix="/ai",
    tags=["AI"],
)


class ChatRequest(BaseModel):
    message: str = Field(
        ...,
        min_length=1,
        max_length=4000,
    )


class ChatResponse(BaseModel):
    response: str


@router.post("/chat", response_model=ChatResponse)
async def chat_endpoint(
    request: ChatRequest,
    current_user: dict[str, Any] = Depends(get_current_user),
):
    try:
        response = chat(request.message)

        return ChatResponse(
            response=response,
        )

    except Exception as exc:
        raise HTTPException(
            status_code=500,
            detail=f"AI service error: {exc}",
        ) from exc
