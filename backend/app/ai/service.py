from app.ai.llm import get_llm
from app.ai.prompts import SMARTSCHOOL_PROMPT


def _extract_text(content) -> str:
    if isinstance(content, str):
        return content

    if isinstance(content, list):
        parts = []

        for item in content:
            if isinstance(item, str):
                parts.append(item)

            elif isinstance(item, dict):
                text = item.get("text")
                if text:
                    parts.append(text)

        return "".join(parts)

    return str(content)


def chat(message: str) -> str:
    llm = get_llm()

    chain = SMARTSCHOOL_PROMPT | llm

    response = chain.invoke(
        {
            "message": message,
        }
    )

    return _extract_text(response.content)
